using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiSe.Audio;

public sealed class Gate2UnifiedAudioEngine : IDisposable
{
    // VoiSee 13.0.1 latency foundation. Keep the device buffers bounded and
    // substantially shorter than the previous 50 ms render target. These are
    // requested WASAPI latencies; actual device/driver latency is measured
    // externally and may differ.
    private const int CaptureLatencyTargetMs = 10;
    private const int RenderLatencyTargetMs = 20;
    private const int MaxMicrophoneQueueMs = 250;
    private readonly MMDevice _inputDevice;
    private readonly MMDevice _virtualOutputDevice;
    private readonly MMDevice? _monitorDevice;
    private EffectSettings _settings;
    private readonly WaveFormat _mixFormat;

    private readonly FloatSampleQueue _virtualMicQueue;
    private readonly FloatSampleQueue _monitorMicQueue;
    private readonly SoundboardTransport _soundboard;
    private readonly MediaBridgeTransport _mediaBridge;
    private readonly AudioLatencyTelemetry _latencyTelemetry = new();

    private WasapiCapture? _capture;
    private WasapiOut? _virtualOutput;
    private WasapiOut? _monitorOutput;
    private SimpleVoiceProcessor? _processor;
    private RnNoiseSuppressionProcessor? _rnNoiseSuppressor;
    private DeepFilterNetSuppressionProcessor? _deepFilterNetSuppressor;
    private RouteMixSampleProvider? _virtualProvider;
    private RouteMixSampleProvider? _monitorProvider;
    private ProcessLoopbackCapture? _mediaBridgeCapture;
    private string? _mediaBridgeError;
    private bool _virtualMicMuted;
    private readonly object _voiceMonitorRouteSync = new();
    private bool _voiceMonitorRouteEnabled;
    private volatile bool _fullProcessingEnabled;
    private AiPerformanceProfile _performanceProfile = AiPerformanceProfile.Balanced;
    private readonly object _aiVoiceSync = new();
    private IAiVoiceRealtimeProcessor? _aiVoiceProcessor;
    private AiVoiceRuntimeConfiguration? _aiVoiceConfiguration;
    private bool _disposed;

    public event Action<AudioProcessingPath>? ProcessingPathChanged;
    public event Action<AiVoiceRuntimeStatus>? AiVoiceRuntimeStatusChanged;

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

        // Bound microphone route backlog to 250 ms. The previous ~2 second queue could
        // turn a temporary render stall into seconds of perceived voice latency. Old
        // samples are still dropped rather than allowing latency to grow without bound.
        var maxSamples = (int)Math.Ceiling(_mixFormat.SampleRate * _mixFormat.Channels * (MaxMicrophoneQueueMs / 1000.0));
        _virtualMicQueue = new FloatSampleQueue(maxSamples);
        _monitorMicQueue = new FloatSampleQueue(maxSamples);
        _soundboard = new SoundboardTransport(_mixFormat);
        _mediaBridge = new MediaBridgeTransport(_mixFormat);
        _fullProcessingEnabled = RequiresFullProcessing(settings);
    }

    public WaveFormat MixFormat => _mixFormat;
    public AudioProcessingPath CurrentProcessingPath
    {
        get
        {
            var aiActive = IsAiVoiceRuntimeActive;
            if (aiActive)
            {
                return _fullProcessingEnabled ? AudioProcessingPath.AiFull : AudioProcessingPath.Ai;
            }

            return _fullProcessingEnabled ? AudioProcessingPath.Full : AudioProcessingPath.Fast;
        }
    }
    public AiPerformanceProfile PerformanceProfile => _performanceProfile;
    public bool IsAiVoiceRuntimeActive
    {
        get
        {
            lock (_aiVoiceSync)
            {
                return _aiVoiceProcessor is not null
                    && _aiVoiceProcessor.GetStatus().State != AiVoiceRuntimeState.Disabled;
            }
        }
    }
    public AiVoiceRuntimeStatus GetAiVoiceRuntimeStatus()
    {
        lock (_aiVoiceSync)
        {
            return _aiVoiceProcessor?.GetStatus() ?? AiVoiceRuntimeStatus.Disabled;
        }
    }
    public int CaptureLatencyTarget => CaptureLatencyTargetMs;
    public int RenderLatencyTarget => RenderLatencyTargetMs;
    public AudioLatencySnapshot GetLatencySnapshot() => _latencyTelemetry.Snapshot();

    public void SetPerformanceProfile(AiPerformanceProfile profile)
    {
        _performanceProfile = profile;

        AiVoiceRuntimeConfiguration? current;
        lock (_aiVoiceSync)
        {
            current = _aiVoiceConfiguration;
        }

        if (current is not null && current.PerformanceProfile != profile)
        {
            _ = RestartAiVoiceForProfileAsync(current with { PerformanceProfile = profile });
        }
    }

    private static IAiVoiceRealtimeProcessor CreateAiVoiceProcessor(AiVoiceRuntimeConfiguration configuration)
        => configuration.Backend switch
        {
            AiVoiceRuntimeBackend.Rvc => new RvcRealtimeAiVoiceProcessor(configuration),
            _ => throw new NotSupportedException($"AI Voice backend '{configuration.Backend}' is not supported by this build."),
        };

    public async Task EnableAiVoiceAsync(AiVoiceRuntimeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var effective = configuration with { PerformanceProfile = _performanceProfile };
        var candidate = CreateAiVoiceProcessor(effective);
        candidate.StatusChanged += OnAiVoiceRuntimeStatusChanged;

        try
        {
            await candidate.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            candidate.StatusChanged -= OnAiVoiceRuntimeStatusChanged;
            candidate.Dispose();
            throw;
        }

        IAiVoiceRealtimeProcessor? previous;
        var oldPath = CurrentProcessingPath;
        lock (_aiVoiceSync)
        {
            if (_disposed)
            {
                candidate.StatusChanged -= OnAiVoiceRuntimeStatusChanged;
                candidate.Dispose();
                throw new ObjectDisposedException(nameof(Gate2UnifiedAudioEngine));
            }
            previous = _aiVoiceProcessor;
            _aiVoiceProcessor = candidate;
            _aiVoiceConfiguration = effective;
        }
        if (previous is not null)
        {
            previous.StatusChanged -= OnAiVoiceRuntimeStatusChanged;
            previous.Dispose();
        }

        var newPath = CurrentProcessingPath;
        if (oldPath != newPath)
        {
            ProcessingPathChanged?.Invoke(newPath);
        }
        AiVoiceRuntimeStatusChanged?.Invoke(candidate.GetStatus());
    }

    public void DisableAiVoice()
    {
        var oldPath = CurrentProcessingPath;
        IAiVoiceRealtimeProcessor? previous;
        lock (_aiVoiceSync)
        {
            previous = _aiVoiceProcessor;
            _aiVoiceProcessor = null;
            _aiVoiceConfiguration = null;
        }
        if (previous is not null)
        {
            previous.StatusChanged -= OnAiVoiceRuntimeStatusChanged;
            previous.Dispose();
        }
        var newPath = CurrentProcessingPath;
        if (oldPath != newPath)
        {
            ProcessingPathChanged?.Invoke(newPath);
        }
        AiVoiceRuntimeStatusChanged?.Invoke(AiVoiceRuntimeStatus.Disabled);
    }

    private async Task RestartAiVoiceForProfileAsync(AiVoiceRuntimeConfiguration configuration)
    {
        try
        {
            await EnableAiVoiceAsync(configuration).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AiVoiceRuntimeStatusChanged?.Invoke(new AiVoiceRuntimeStatus(
                AiVoiceRuntimeState.Faulted,
                configuration.VoiceId,
                configuration.VoiceName,
                $"AI runtime profile switch failed: {ex.Message}",
                0, 0.0, 0.0, 0.0, 0.0, 0, 0));
        }
    }

    private void OnAiVoiceRuntimeStatusChanged(AiVoiceRuntimeStatus status)
    {
        AiVoiceRuntimeStatusChanged?.Invoke(status);
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
        _capture = new WasapiCapture(_inputDevice, true, CaptureLatencyTargetMs);
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

        _rnNoiseSuppressor = new RnNoiseSuppressionProcessor(
            _settings.NoiseSuppressionMode == NoiseSuppressionMode.RnNoise,
            _settings.NoiseSuppressionStrength);
        _deepFilterNetSuppressor = new DeepFilterNetSuppressionProcessor(
            _settings.NoiseSuppressionMode == NoiseSuppressionMode.DeepFilterNet,
            _settings.NoiseSuppressionStrength);
        _processor = new SimpleVoiceProcessor(_settings);

        _virtualProvider = new RouteMixSampleProvider(
            _mixFormat,
            AudioRoute.VirtualMicrophone,
            _virtualMicQueue,
            _soundboard,
            _mediaBridge,
            _settings);
        _virtualProvider.SetVirtualMicMuted(_virtualMicMuted);

        _virtualOutput = new WasapiOut(_virtualOutputDevice, AudioClientShareMode.Shared, true, RenderLatencyTargetMs);
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

            _monitorOutput = new WasapiOut(_monitorDevice, AudioClientShareMode.Shared, true, RenderLatencyTargetMs);
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
        _rnNoiseSuppressor?.UpdateSettings(
            settings.NoiseSuppressionMode == NoiseSuppressionMode.RnNoise,
            settings.NoiseSuppressionStrength);
        _deepFilterNetSuppressor?.UpdateSettings(
            settings.NoiseSuppressionMode == NoiseSuppressionMode.DeepFilterNet,
            settings.NoiseSuppressionStrength);

        var shouldUseFullPath = RequiresFullProcessing(settings);
        var pathChanged = shouldUseFullPath != _fullProcessingEnabled;
        if (pathChanged && shouldUseFullPath)
        {
            // Stateful delay/pitch effects may contain audio from the previous Full
            // session. Recreate the processor when re-entering Full so stale audio
            // can never leak across a Fast -> Full transition.
            _processor = new SimpleVoiceProcessor(settings);
        }
        else
        {
            _processor?.UpdateSettings(settings);
        }

        _fullProcessingEnabled = shouldUseFullPath;
        _virtualProvider?.UpdateSettings(settings);
        _monitorProvider?.UpdateSettings(settings);

        if (pathChanged)
        {
            ProcessingPathChanged?.Invoke(CurrentProcessingPath);
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

    public void Stop()
    {
        DisableAiVoice();
        StopMediaBridge();
        _soundboard.Stop();
        _capture?.StopRecording();
        _virtualOutput?.Stop();
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
            var captureBufferMs = _capture.WaveFormat.AverageBytesPerSecond > 0
                ? (e.BytesRecorded * 1000.0) / _capture.WaveFormat.AverageBytesPerSecond
                : CaptureLatencyTargetMs;

            var stageStart = Stopwatch.GetTimestamp();
            var targetSamples = AudioFormatConverter.CaptureBufferToTargetStereoFloat(
                e.Buffer,
                e.BytesRecorded,
                _capture.WaveFormat,
                _mixFormat);
            var conversionMs = ElapsedMilliseconds(stageStart);

            // Noise suppression remains microphone-only and remains part of Fast Path.
            stageStart = Stopwatch.GetTimestamp();
            switch (_settings.NoiseSuppressionMode)
            {
                case NoiseSuppressionMode.RnNoise:
                    _rnNoiseSuppressor?.ProcessInPlace(targetSamples);
                    break;
                case NoiseSuppressionMode.DeepFilterNet:
                    _deepFilterNetSuppressor?.ProcessInPlace(targetSamples);
                    break;
            }
            var noiseSuppressionMs = ElapsedMilliseconds(stageStart);

            // AI Voice sits after microphone cleanup and before the ordinary DSP chain.
            // Inference itself runs on a worker process; the capture callback only enqueues
            // the current microphone block and consumes already-converted output.
            var aiMs = 0.0;
            IAiVoiceRealtimeProcessor? aiVoiceProcessor;
            lock (_aiVoiceSync)
            {
                aiVoiceProcessor = _aiVoiceProcessor;
            }
            if (aiVoiceProcessor is not null)
            {
                aiVoiceProcessor.ProcessInPlace(targetSamples);
                aiMs = aiVoiceProcessor.EstimatedStageLatencyMs;
            }

            // The regular Voice Changer DSP remains after AI Voice.
            var dspMs = 0.0;
            var processingPath = CurrentProcessingPath;
            if (_fullProcessingEnabled)
            {
                stageStart = Stopwatch.GetTimestamp();
                _processor.ProcessInPlace(targetSamples);
                dspMs = ElapsedMilliseconds(stageStart);
            }

            _virtualMicQueue.Add(targetSamples);

            // Voice Monitor Off is a hard route disconnect, not only a zero gain.
            lock (_voiceMonitorRouteSync)
            {
                if (_voiceMonitorRouteEnabled)
                {
                    _monitorMicQueue.Add(targetSamples);
                }
            }

            // Queue latency is backlog ahead of the just-captured block. Excluding
            // the current block avoids double-counting capture-buffer duration.
            var virtualQueuedMs = SamplesToMilliseconds(Math.Max(0, _virtualMicQueue.Count - targetSamples.Length));
            var monitorQueuedMs = SamplesToMilliseconds(Math.Max(0, _monitorMicQueue.Count - targetSamples.Length));
            _latencyTelemetry.Record(
                processingPath,
                captureBufferMs,
                conversionMs,
                noiseSuppressionMs,
                aiMs,
                dspMs,
                virtualQueuedMs,
                monitorQueuedMs,
                RenderLatencyTargetMs,
                _virtualMicQueue.DroppedSampleCount,
                _monitorMicQueue.DroppedSampleCount);
        }
        catch (NotSupportedException ex)
        {
            Console.Error.WriteLine($"Capture format is not supported: {ex.Message}");
        }
    }

    private static bool RequiresFullProcessing(EffectSettings settings)
    {
        // EffectOrder is built from active, non-bypassed Processing Chain cards.
        // Mixer limiting/output gain remain outside SimpleVoiceProcessor, so an
        // empty chain can safely bypass it without changing route semantics.
        return settings.EffectOrder is { Count: > 0 };
    }

    private double SamplesToMilliseconds(int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return 0.0;
        }

        return sampleCount * 1000.0 / (_mixFormat.SampleRate * _mixFormat.Channels);
    }

    private static double ElapsedMilliseconds(long startTimestamp)
    {
        var elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
        return elapsedTicks * 1000.0 / Stopwatch.Frequency;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _capture?.Dispose();
        _virtualOutput?.Dispose();
        _monitorOutput?.Dispose();
        _rnNoiseSuppressor?.Dispose();
        _deepFilterNetSuppressor?.Dispose();
    }
}
