using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiSe.Audio;

public sealed class Gate0AudioEngine : IDisposable
{
    private readonly MMDevice _inputDevice;
    private readonly MMDevice _virtualOutputDevice;
    private readonly MMDevice? _monitorDevice;
    private readonly EffectSettings _settings;

    private WasapiCapture? _capture;
    private WasapiOut? _virtualOutput;
    private WasapiOut? _monitorOutput;
    private BufferedWaveProvider? _virtualBuffer;
    private BufferedWaveProvider? _monitorBuffer;
    private SimpleVoiceProcessor? _processor;
    private bool _disposed;

    public Gate0AudioEngine(
        MMDevice inputDevice,
        MMDevice virtualOutputDevice,
        MMDevice? monitorDevice,
        EffectSettings settings)
    {
        _inputDevice = inputDevice;
        _virtualOutputDevice = virtualOutputDevice;
        _monitorDevice = monitorDevice;
        _settings = settings;
    }

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

        var format = _capture.WaveFormat;
        if (!PcmFloatConverter.CanProcess(format))
        {
            Console.WriteLine($"Warning: processing is not supported for format {format.Encoding} {format.BitsPerSample} bit. Prototype may need resampling/conversion.");
        }

        _virtualBuffer = CreateBuffer(format);
        _virtualOutput = new WasapiOut(_virtualOutputDevice, AudioClientShareMode.Shared, true, 50);
        _virtualOutput.Init(_virtualBuffer);
        _virtualOutput.Play();

        if (_monitorDevice is not null)
        {
            _monitorBuffer = CreateBuffer(format);
            _monitorOutput = new WasapiOut(_monitorDevice, AudioClientShareMode.Shared, true, 50);
            _monitorOutput.Init(_monitorBuffer);
            _monitorOutput.Play();
        }

        _processor = new SimpleVoiceProcessor(_settings);
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture?.StopRecording();
        _virtualOutput?.Stop();
        _monitorOutput?.Stop();
    }

    private static BufferedWaveProvider CreateBuffer(WaveFormat format)
    {
        return new BufferedWaveProvider(format)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true
        };
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_processor is null || _virtualBuffer is null)
        {
            return;
        }

        byte[] outputBytes;
        try
        {
            var samples = PcmFloatConverter.ToFloatArray(e.Buffer, e.BytesRecorded, _capture!.WaveFormat);
            _processor.ProcessInPlace(samples);
            outputBytes = PcmFloatConverter.FromFloatArray(samples, _capture.WaveFormat);
        }
        catch (NotSupportedException)
        {
            outputBytes = e.Buffer.Take(e.BytesRecorded).ToArray();
        }

        _virtualBuffer.AddSamples(outputBytes, 0, outputBytes.Length);
        _monitorBuffer?.AddSamples(outputBytes, 0, outputBytes.Length);
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
