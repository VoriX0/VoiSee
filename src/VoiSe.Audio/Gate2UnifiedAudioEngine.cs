using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiSe.Audio;

public sealed class Gate2UnifiedAudioEngine : IAudioSession
{
    private readonly MMDevice _inputDevice;
    private readonly MMDevice _virtualOutputDevice;
    private readonly MMDevice? _monitorDevice;
    private EffectSettings _settings;
    private readonly WaveFormat _mixFormat;

    private readonly FloatSampleQueue _virtualMicQueue;
    private readonly FloatSampleQueue _monitorMicQueue;
    private readonly SoundboardTransport _soundboard;
    private readonly MediaBridgeTransport _mediaBridge;

    private WasapiCapture? _capture;
    private WasapiOut? _virtualOutput;
    private WasapiOut? _monitorOutput;
    private VoiceProcessingChain? _processor;
    private readonly MicrophoneEqualizer _equalizer;
    private CaptureSampleConverter? _captureConverter;
    private double _lastProcessingMs;
    private RnNoiseSuppressionProcessor? _rnNoiseSuppressor;
    private DeepFilterNetSuppressionProcessor? _deepFilterNetSuppressor;
    private WebRtcEchoCanceller? _echoCanceller;
    private RoomEchoReducer? _roomEchoReducer;
    private EchoReferenceCapture? _echoReferenceCapture;
    private CaptureSampleConverter? _echoReferenceConverter;
    private MMDevice? _echoReferenceDevice;
    private readonly float[] _echoSilence = new float[960];
    private long _lastEchoReference;
    private string? _echoError;
    private string? _echoReferenceName;
    public string? EchoCancellationError => _echoError;
    public bool EchoCancellationAvailable => _echoCanceller is not null && _echoError is null;
    public string? EchoReferenceDeviceName => _echoReferenceName;
    private RouteMixSampleProvider? _virtualProvider;
    private RouteMixSampleProvider? _monitorProvider;
    private ProcessLoopbackCapture? _mediaBridgeCapture;
    private string? _mediaBridgeError;
    private bool _virtualMicMuted;
    private readonly object _voiceMonitorRouteSync = new();
    private bool _voiceMonitorRouteEnabled;
    private volatile bool _disposed, _running;
    private bool _started;
    private bool _shutdownComplete;
    private readonly CancellationTokenSource _sessionCancellation = new();
    private readonly bool _ownsDevices;
    private readonly object _lifecycle = new();
    private readonly object _processing = new();
    private long _mediaGeneration;
    public event Action<string>? Faulted;
    public string? LastError { get; private set; }
    private int _faultReported;
    private void ReportFault(Exception error)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0) return;
        _running = false;
        LastError = error.Message;
        try { Faulted?.Invoke(error.Message); } catch { /* No exception escapes a native callback. */ }
    }

    public Gate2UnifiedAudioEngine(
        MMDevice inputDevice,
        MMDevice virtualOutputDevice,
        MMDevice? monitorDevice,
        EffectSettings settings, bool ownsDevices = false, EqualizerSettings? equalizer = null)
    {
        _ownsDevices = ownsDevices;
        _inputDevice = inputDevice;
        _virtualOutputDevice = virtualOutputDevice;
        _monitorDevice = monitorDevice;
        _settings = settings;
        _voiceMonitorRouteEnabled = settings.VoiceMonitorGain > 0.0001f;
        _mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        _equalizer = new MicrophoneEqualizer(_mixFormat.SampleRate, _mixFormat.Channels, equalizer);

        // Discard old audio on a render stall instead of replaying a two-second backlog.
        var maxSamples = AudioTiming.QueueSamples(_mixFormat.SampleRate, _mixFormat.Channels);
        _virtualMicQueue = new FloatSampleQueue(maxSamples);
        _monitorMicQueue = new FloatSampleQueue(maxSamples);
        _soundboard = new SoundboardTransport(_mixFormat, monitorDevice is not null);
        _mediaBridge = new MediaBridgeTransport(_mixFormat);
    }

    public WaveFormat MixFormat => _mixFormat;
    public string? SoundPlaybackError => _soundboard.LastError;
    public AudioTimingSnapshot GetTimingSnapshot()
    {
        lock (_processing)
            return new AudioTimingSnapshot(_processor?.IsFastPath ?? true, _lastProcessingMs,
                _virtualMicQueue.Count * 1000.0 / (_mixFormat.SampleRate * _mixFormat.Channels),
                _mediaBridge.QueuedMs, _virtualMicQueue.DroppedSampleCount, AudioTiming.CaptureMs, AudioTiming.RenderMs);
    }

    public bool NoiseSuppressionAvailable => _settings.NoiseSuppressionMode switch
    {
        NoiseSuppressionMode.RnNoise => _rnNoiseSuppressor?.IsAvailable ?? false,
        NoiseSuppressionMode.DeepFilterNet => _deepFilterNetSuppressor?.IsAvailable ?? false,
        _ => true,
    };

    public string? NoiseSuppressionError => _settings.NoiseSuppressionMode switch
    {
        NoiseSuppressionMode.RnNoise => _rnNoiseSuppressor?.InitializationError,
        NoiseSuppressionMode.DeepFilterNet => _deepFilterNetSuppressor?.InitializationError,
        _ => null,
    };

    public string ActiveNoiseSuppressionName => _settings.NoiseSuppressionMode switch
    {
        NoiseSuppressionMode.RnNoise => "RNNoise",
        NoiseSuppressionMode.DeepFilterNet => "DeepFilterNet",
        _ => "Off",
    };

    public bool VoiceMonitorRouteEnabled
    {
        get
        {
            lock (_voiceMonitorRouteSync)
            {
                return _voiceMonitorRouteEnabled;
            }
        }
    }

    public void Start()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) throw new InvalidOperationException("Create a new engine for a new audio session.");
            _started = true;
            try
            {
                StartCore();
                if (_faultReported != 0) throw new InvalidOperationException(LastError ?? "Audio startup failed.");
                _running = true;
            }
            catch (Exception ex) { ReportFault(ex); throw; }
        }
    }
    private void StartCore()
    {
        _capture = new WasapiCapture(_inputDevice, true, AudioTiming.CaptureMs);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null)
            {
                if (_running) ReportFault(args.Exception);
            }
        };

        if (!PcmFloatConverter.CanProcess(_capture.WaveFormat))
        {
            Console.WriteLine($"Warning: capture processing is not supported for format {_capture.WaveFormat.Encoding} {_capture.WaveFormat.BitsPerSample} bit.");
            Console.WriteLine("Gate 2 expects IEEE float 32-bit or PCM 16-bit capture format.");
        }

        _rnNoiseSuppressor = new RnNoiseSuppressionProcessor(
            _settings.NoiseSuppressionMode == NoiseSuppressionMode.RnNoise,
            _settings.NoiseSuppressionStrength);
        _deepFilterNetSuppressor = new DeepFilterNetSuppressionProcessor(
            _settings.NoiseSuppressionMode == NoiseSuppressionMode.DeepFilterNet,
            _settings.NoiseSuppressionStrength);
        _processor = new VoiceProcessingChain(_settings);
        _captureConverter = new CaptureSampleConverter(_capture.WaveFormat, _mixFormat);
        if (_settings.RoomEchoReductionEnabled) _roomEchoReducer = new RoomEchoReducer();
        if (_settings.EchoCancellationEnabled) StartEchoReference();

        _virtualProvider = new RouteMixSampleProvider(
            _mixFormat,
            AudioRoute.VirtualMicrophone,
            _virtualMicQueue,
            _soundboard,
            _mediaBridge,
            _settings);
        _virtualProvider.SetVirtualMicMuted(_virtualMicMuted);

        _virtualOutput = new WasapiOut(_virtualOutputDevice, AudioClientShareMode.Shared, true, AudioTiming.RenderMs);
        _virtualOutput.PlaybackStopped += OnPlaybackStopped;
        _virtualOutput.Init(new SampleToWaveProvider(_virtualProvider));
        _virtualOutput.Play();

        if (_monitorDevice is not null)
        {
            _monitorProvider = new RouteMixSampleProvider(
                _mixFormat,
                AudioRoute.Monitor,
                _monitorMicQueue,
                _soundboard,
                _mediaBridge,
                _settings);

            _monitorOutput = new WasapiOut(_monitorDevice, AudioClientShareMode.Shared, true, AudioTiming.RenderMs);
            _monitorOutput.PlaybackStopped += OnPlaybackStopped;
            _monitorOutput.Init(new SampleToWaveProvider(_monitorProvider));
            _monitorOutput.Play();
        }

        _capture.StartRecording();
    }

    private void StartEchoReference()
    {
        try
        {
            using var catalog = new AudioDeviceCatalog();
            _echoReferenceDevice = catalog.FindRenderDevice(_settings.EchoReferenceDeviceId)
                ?? throw new InvalidOperationException("Устройство воспроизведения недоступно. Проверьте настройки звука Windows.");
            if (_echoReferenceDevice.ID == _virtualOutputDevice.ID)
                throw new InvalidOperationException("В настройках звука Windows выберите колонки или наушники устройством воспроизведения по умолчанию.");
            _echoReferenceName = _echoReferenceDevice.FriendlyName;
            _echoCanceller = new WebRtcEchoCanceller();
            _echoReferenceCapture = new EchoReferenceCapture(_echoReferenceDevice);
            _echoReferenceConverter = new CaptureSampleConverter(_echoReferenceCapture.WaveFormat, _mixFormat);
            _echoReferenceCapture.DataAvailable += OnEchoReferenceAvailable;
            _echoReferenceCapture.RecordingStopped += (_, args) =>
            {
                if (_running || args.Exception is not null)
                    lock (_processing) _echoError = args.Exception?.Message ?? "Захват звука устройства остановлен. Обновите список устройств.";
            };
            _echoReferenceCapture.StartRecording();
        }
        catch (Exception ex)
        {
            _echoError = ex.Message;
            Cleanup(() => _echoReferenceCapture?.Dispose()); _echoReferenceCapture = null;
            Cleanup(() => _echoCanceller?.Dispose()); _echoCanceller = null;
            Cleanup(() => _echoReferenceDevice?.Dispose()); _echoReferenceDevice = null;
        }
    }
    private void OnEchoReferenceAvailable(object? sender, WaveInEventArgs args)
    {
        lock (_processing)
        {
            if (_disposed || _echoCanceller is null || _echoReferenceConverter is null || _echoError is not null || args.BytesRecorded == 0) return;
            try
            {
                _echoCanceller.AddReference(_echoReferenceConverter.Convert(args.Buffer, args.BytesRecorded));
                _lastEchoReference = Stopwatch.GetTimestamp();
            }
            catch (Exception ex) { _echoError = ex.Message; }
        }
    }


    public async Task StartMediaBridgeAsync(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        long generation;
        StopMediaBridge();
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed || !_running, this);
            generation = ++_mediaGeneration;
        }
        var capture = new ProcessLoopbackCapture();
        capture.SamplesAvailable += samples => { if (ReferenceEquals(_mediaBridgeCapture, capture)) _mediaBridge.AddSamples(samples); };
        capture.CaptureStopped += error => { if (ReferenceEquals(_mediaBridgeCapture, capture) && error is not null) _mediaBridgeError = error.Message; };
        try
        {
            await capture.StartAsync((uint)processId).ConfigureAwait(false);
            lock (_lifecycle)
            {
                if (_disposed || !_running || generation != _mediaGeneration)
                    throw new OperationCanceledException("Media capture was stopped before activation completed.");
                _mediaBridgeCapture = capture;
            }
        }
        catch { capture.Dispose(); throw; }
    }

    public void StopMediaBridge()
    {
        ProcessLoopbackCapture? capture;
        lock (_lifecycle)
        {
            _mediaGeneration++;
            capture = _mediaBridgeCapture; _mediaBridgeCapture = null; _mediaBridgeError = null;
        }
        try { capture?.Dispose(); }
        finally { _mediaBridge.Clear(); }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    { if (_running && args.Exception is not null) ReportFault(args.Exception); }

    public bool ToggleMediaBridgePause()
    {
        var paused = !_mediaBridge.IsPaused;
        _mediaBridge.SetPaused(paused);
        return paused;
    }

    public void SetMediaBridgePaused(bool paused)
    {
        _mediaBridge.SetPaused(paused);
    }

    public void UpdateMediaBridgeVolume(float volume)
    {
        _mediaBridge.SetVolume(volume);
    }

    public bool IsMediaBridgeBroadcasting => _mediaBridgeCapture?.IsCapturing == true;
    public bool IsMediaBridgePaused => _mediaBridge.IsPaused;
    public float MediaBridgeSourcePeak => _mediaBridge.GetSourceDisplayPeak();
    public float MediaBridgeOutputPeak => _mediaBridge.GetOutputDisplayPeak();
    public string? MediaBridgeError => _mediaBridgeError;

    public void PlaySound(string filePath, float virtualVolume, float monitorVolume, int virtualDelayMs, bool loop = false, string? playbackKey = null)
    {
        _soundboard.Play(filePath, virtualVolume, monitorVolume, virtualDelayMs, loop, playbackKey);
    }

    public Task PlaySoundAsync(string filePath, float virtualVolume, float monitorVolume, int virtualDelayMs,
        bool loop = false, string? playbackKey = null, CancellationToken cancellationToken = default) =>
        _soundboard.PlayAsync(filePath, virtualVolume, monitorVolume, virtualDelayMs, loop, playbackKey, cancellationToken);

    public async Task PreloadSoundAsync(string filePath, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource linked;
        lock (_lifecycle)
        {
            if (_disposed || !_running) return;
            linked = CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellation.Token, cancellationToken);
        }
        using (linked) await SoundFileLoader.PreloadToFormatAsync(filePath, _mixFormat, linked.Token).ConfigureAwait(false);
    }

    public void StopSound(string? playbackKey = null)
    {
        _soundboard.Stop(playbackKey);
    }

    public void SeekSound(double seconds, string? playbackKey = null)
    {
        _soundboard.Seek(seconds, playbackKey);
    }

    public bool ToggleSoundPause(string? playbackKey = null)
    {
        return _soundboard.TogglePause(playbackKey);
    }

    public SoundboardStatus GetSoundStatus(string? playbackKey = null)
    {
        return _soundboard.GetStatus(playbackKey);
    }

    public void UpdateEqualizer(EqualizerSettings settings)
    {
        lock (_processing)
        {
            if (!_disposed) _equalizer.Update(settings);
        }
    }

    public void UpdateEffectSettings(EffectSettings settings)
    {
        lock (_processing)
        {
            if (_disposed) return;
            _settings = settings;
            SetVoiceMonitorEnabled(settings.VoiceMonitorGain > 0.0001f);
            _rnNoiseSuppressor?.UpdateSettings(
                settings.NoiseSuppressionMode == NoiseSuppressionMode.RnNoise,
                settings.NoiseSuppressionStrength);
            _deepFilterNetSuppressor?.UpdateSettings(
                settings.NoiseSuppressionMode == NoiseSuppressionMode.DeepFilterNet,
                settings.NoiseSuppressionStrength);
            _processor?.Update(settings);
            _virtualProvider?.UpdateSettings(settings);
            _monitorProvider?.UpdateSettings(settings);
        }
    }

    /// <summary>
    /// Hard-connects or disconnects processed microphone audio from the physical
    /// monitor route. When disabled, processed voice samples are not queued for
    /// the monitor output at all. VoiceMonitorGain remains a second safety layer.
    /// SoundBoard monitoring stays independent.
    /// </summary>
    public void SetVoiceMonitorEnabled(bool enabled)
    {
        lock (_voiceMonitorRouteSync)
        {
            if (_voiceMonitorRouteEnabled == enabled)
            {
                return;
            }

            _voiceMonitorRouteEnabled = enabled;
            _monitorMicQueue.Clear();
        }
    }

    public void UpdateSoundVolumes(float virtualVolume, float monitorVolume, string? playbackKey = null)
    {
        _soundboard.UpdateVolumes(virtualVolume, monitorVolume, playbackKey);
    }

    public void SetVirtualMicMuted(bool muted)
    {
        _virtualMicMuted = muted;
        _virtualProvider?.SetVirtualMicMuted(muted);
    }

    public void UpdateSoundLoop(bool loop, string? playbackKey = null)
    {
        _soundboard.UpdateLoop(loop, playbackKey);
    }

    private static void Cleanup(Action action)
    {
        try { action(); } catch (Exception ex) { Console.Error.WriteLine("Audio cleanup: " + ex.Message); }
    }
    public void Stop()
    {
        lock (_lifecycle)
        {
            if (_shutdownComplete) return;
            _running = false;
            _sessionCancellation.Cancel();
            Cleanup(StopMediaBridge);
            Cleanup(() => _soundboard.Stop());
            Cleanup(() => _capture?.StopRecording());
            Cleanup(() => _echoReferenceCapture?.StopRecording());
            Cleanup(() => _virtualOutput?.Stop());
            Cleanup(() => _monitorOutput?.Stop());
            lock (_processing) { _virtualMicQueue.Clear(); _monitorMicQueue.Clear(); }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_processing)
        {
            if (_disposed || !_running || _processor is null || _capture is null || _captureConverter is null)
            {
                return;
            }

            try
            {
                var started = Stopwatch.GetTimestamp();
                var targetSamples = _captureConverter.Convert(e.Buffer, e.BytesRecorded);
                if (_echoCanceller is not null && _echoError is null)
                {
                    try
                    {
                        // Loopback sends no packets when the endpoint is idle.
                        // Clock silence into the reverse path instead of reusing stale sound.
                        if (Stopwatch.GetElapsedTime(_lastEchoReference).TotalMilliseconds > 60)
                            for (var offset = 0; offset < targetSamples.Length; offset += _echoSilence.Length)
                                _echoCanceller.AddReference(_echoSilence.AsSpan(0, Math.Min(_echoSilence.Length, targetSamples.Length - offset)));
                        _echoCanceller.Process(targetSamples);
                    }
                    catch (Exception ex) { _echoError = ex.Message; }
                }
                _roomEchoReducer?.Process(targetSamples);

                // Noise suppression is intentionally microphone-only and remains
                // outside voice presets. Exactly one cleanup engine runs before
                // gate/compressor and the preset-controlled effects.
                switch (_settings.NoiseSuppressionMode)
                {
                    case NoiseSuppressionMode.RnNoise:
                        _rnNoiseSuppressor?.ProcessInPlace(targetSamples);
                        break;
                    case NoiseSuppressionMode.DeepFilterNet:
                        _deepFilterNetSuppressor?.ProcessInPlace(targetSamples);
                        break;
                }
                _equalizer.Process(targetSamples);
                _processor.Process(targetSamples);
                _virtualMicQueue.Add(targetSamples);

                // Voice Monitor Off is a hard route disconnect, not only a zero gain.
                // This prevents processed microphone audio and stale queued samples
                // from reaching a physical render session that an application screen
                // share may capture. SoundBoard monitoring uses its own route.
                lock (_voiceMonitorRouteSync)
                {
                    if (_voiceMonitorRouteEnabled)
                    {
                        _monitorMicQueue.Add(targetSamples);
                    }
                }
                _lastProcessingMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            catch (Exception ex) { ReportFault(ex); }
        }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            // Native capture disposal may wait for callbacks. Never wait while holding
            // the DSP lock a final callback needs in order to observe _disposed.
            Cleanup(() => _capture?.Dispose());
            Cleanup(() => _echoReferenceCapture?.Dispose());
            Cleanup(() => _virtualOutput?.Dispose());
            Cleanup(() => _monitorOutput?.Dispose());
            lock (_processing)
            {
                Cleanup(() => _echoCanceller?.Dispose());
                Cleanup(() => _echoReferenceDevice?.Dispose());
                Cleanup(() => _rnNoiseSuppressor?.Dispose());
                Cleanup(() => _deepFilterNetSuppressor?.Dispose());
                Cleanup(_soundboard.Dispose);
                if (_ownsDevices)
                {
                    Cleanup(_inputDevice.Dispose); Cleanup(_virtualOutputDevice.Dispose);
                    Cleanup(() => _monitorDevice?.Dispose());
                }
            }
            _shutdownComplete = true;
            _sessionCancellation.Dispose();
        }
    }
}
