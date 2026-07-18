using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiSe.Audio;

public sealed class Gate2UnifiedAudioEngine : IDisposable
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
    private IsolatedVirtualMicOutput? _isolatedVirtualOutput;
    private WasapiOut? _monitorOutput;
    private SimpleVoiceProcessor? _processor;
    private RouteMixSampleProvider? _virtualProvider;
    private RouteMixSampleProvider? _monitorProvider;
    private ProcessLoopbackCapture? _mediaBridgeCapture;
    private string? _mediaBridgeError;
    private bool _virtualMicMuted;
    private readonly object _voiceMonitorRouteSync = new();
    private bool _voiceMonitorRouteEnabled;
    private bool _disposed;

    public Gate2UnifiedAudioEngine(
        MMDevice inputDevice,
        MMDevice virtualOutputDevice,
        MMDevice? monitorDevice,
        EffectSettings settings)
    {
        _inputDevice = inputDevice;
        _virtualOutputDevice = virtualOutputDevice;
        _monitorDevice = monitorDevice;
        _settings = settings;
        _voiceMonitorRouteEnabled = settings.VoiceMonitorGain > 0.0001f;
        _mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

        // Keep up to ~2 seconds of microphone route audio. If an output route falls behind,
        // old samples are dropped instead of increasing latency forever.
        var maxSamples = _mixFormat.SampleRate * _mixFormat.Channels * 2;
        _virtualMicQueue = new FloatSampleQueue(maxSamples);
        _monitorMicQueue = new FloatSampleQueue(maxSamples);
        _soundboard = new SoundboardTransport(_mixFormat);
        _mediaBridge = new MediaBridgeTransport(_mixFormat);
    }

    public WaveFormat MixFormat => _mixFormat;

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

    public bool VirtualMicOutputIsolated => _isolatedVirtualOutput is not null;
    public int? VirtualMicHostProcessId => _isolatedVirtualOutput?.HostProcessId;
    public string? VirtualMicHostError => _isolatedVirtualOutput?.LastError;

    public void Start()
    {
        _capture = new WasapiCapture(_inputDevice, true, 20);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null)
            {
                Console.Error.WriteLine($"Recording stopped with error: {args.Exception.Message}");
            }
        };

        if (!PcmFloatConverter.CanProcess(_capture.WaveFormat))
        {
            Console.WriteLine($"Warning: capture processing is not supported for format {_capture.WaveFormat.Encoding} {_capture.WaveFormat.BitsPerSample} bit.");
            Console.WriteLine("Gate 2 expects IEEE float 32-bit or PCM 16-bit capture format.");
        }

        _processor = new SimpleVoiceProcessor(_settings);

        _virtualProvider = new RouteMixSampleProvider(
            _mixFormat,
            AudioRoute.VirtualMicrophone,
            _virtualMicQueue,
            _soundboard,
            _mediaBridge,
            _settings);
        _virtualProvider.SetVirtualMicMuted(_virtualMicMuted);

        // The virtual microphone render session lives in a detached helper process.
        // This keeps VB-CABLE audio outside the VoiSee UI process tree so an
        // application-window screen share cannot capture it as VoiSee app audio.
        _isolatedVirtualOutput = new IsolatedVirtualMicOutput(_virtualProvider, _virtualOutputDevice.ID);
        _isolatedVirtualOutput.Start();

        if (_monitorDevice is not null)
        {
            _monitorProvider = new RouteMixSampleProvider(
                _mixFormat,
                AudioRoute.Monitor,
                _monitorMicQueue,
                _soundboard,
                _mediaBridge,
                _settings);

            _monitorOutput = new WasapiOut(_monitorDevice, AudioClientShareMode.Shared, true, 50);
            _monitorOutput.Init(new SampleToWaveProvider(_monitorProvider));
            _monitorOutput.Play();
        }

        _capture.StartRecording();
    }


    public async Task StartMediaBridgeAsync(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        StopMediaBridge();
        _mediaBridgeError = null;
        _mediaBridge.Clear();

        var capture = new ProcessLoopbackCapture();
        capture.SamplesAvailable += samples => _mediaBridge.AddSamples(samples);
        capture.CaptureStopped += error =>
        {
            if (error is not null)
            {
                _mediaBridgeError = error.Message;
            }
        };

        try
        {
            await capture.StartAsync((uint)processId).ConfigureAwait(false);
            _mediaBridgeCapture = capture;
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    public void StopMediaBridge()
    {
        var capture = _mediaBridgeCapture;
        _mediaBridgeCapture = null;
        _mediaBridgeError = null;
        try
        {
            capture?.Dispose();
        }
        finally
        {
            _mediaBridge.Clear();
        }
    }

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

    public Task PreloadSoundAsync(string filePath)
    {
        return SoundFileLoader.PreloadToFormatAsync(filePath, _mixFormat);
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

    public void UpdateEffectSettings(EffectSettings settings)
    {
        _settings = settings;
        SetVoiceMonitorEnabled(settings.VoiceMonitorGain > 0.0001f);
        _processor?.UpdateSettings(settings);
        _virtualProvider?.UpdateSettings(settings);
        _monitorProvider?.UpdateSettings(settings);
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

    public void Stop()
    {
        StopMediaBridge();
        _soundboard.Stop();
        _capture?.StopRecording();
        _isolatedVirtualOutput?.Stop();
        _monitorOutput?.Stop();
        _virtualMicQueue.Clear();
        _monitorMicQueue.Clear();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_processor is null || _capture is null)
        {
            return;
        }

        try
        {
            var targetSamples = AudioFormatConverter.CaptureBufferToTargetStereoFloat(
                e.Buffer,
                e.BytesRecorded,
                _capture.WaveFormat,
                _mixFormat);

            _processor.ProcessInPlace(targetSamples);
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
        }
        catch (NotSupportedException ex)
        {
            Console.Error.WriteLine($"Capture format is not supported: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _capture?.Dispose();
        _isolatedVirtualOutput?.Dispose();
        _monitorOutput?.Dispose();
        _disposed = true;
    }
}
