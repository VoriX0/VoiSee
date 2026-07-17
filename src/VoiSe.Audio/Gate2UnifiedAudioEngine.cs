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

    private WasapiCapture? _capture;
    private WasapiOut? _virtualOutput;
    private WasapiOut? _monitorOutput;
    private SimpleVoiceProcessor? _processor;
    private RouteMixSampleProvider? _virtualProvider;
    private RouteMixSampleProvider? _monitorProvider;
    private bool _virtualMicMuted;
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
        _mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

        // Keep up to ~2 seconds of microphone route audio. If an output route falls behind,
        // old samples are dropped instead of increasing latency forever.
        var maxSamples = _mixFormat.SampleRate * _mixFormat.Channels * 2;
        _virtualMicQueue = new FloatSampleQueue(maxSamples);
        _monitorMicQueue = new FloatSampleQueue(maxSamples);
        _soundboard = new SoundboardTransport(_mixFormat);
    }

    public WaveFormat MixFormat => _mixFormat;

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
            _settings);
        _virtualProvider.SetVirtualMicMuted(_virtualMicMuted);

        _virtualOutput = new WasapiOut(_virtualOutputDevice, AudioClientShareMode.Shared, true, 50);
        _virtualOutput.Init(new SampleToWaveProvider(_virtualProvider));
        _virtualOutput.Play();

        if (_monitorDevice is not null)
        {
            _monitorProvider = new RouteMixSampleProvider(
                _mixFormat,
                AudioRoute.Monitor,
                _monitorMicQueue,
                _soundboard,
                _settings);

            _monitorOutput = new WasapiOut(_monitorDevice, AudioClientShareMode.Shared, true, 50);
            _monitorOutput.Init(new SampleToWaveProvider(_monitorProvider));
            _monitorOutput.Play();
        }

        _capture.StartRecording();
    }

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
        _processor?.UpdateSettings(settings);
        _virtualProvider?.UpdateSettings(settings);
        _monitorProvider?.UpdateSettings(settings);
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
            var targetSamples = AudioFormatConverter.CaptureBufferToTargetStereoFloat(
                e.Buffer,
                e.BytesRecorded,
                _capture.WaveFormat,
                _mixFormat);

            _processor.ProcessInPlace(targetSamples);
            _virtualMicQueue.Add(targetSamples);
            _monitorMicQueue.Add(targetSamples);
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
        _virtualOutput?.Dispose();
        _monitorOutput?.Dispose();
        _disposed = true;
    }
}
