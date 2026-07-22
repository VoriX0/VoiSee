using RNNoise.NET;
using System.Threading;

namespace VoiSe.Audio;

/// <summary>
/// Streaming RNNoise adapter for VoiSee's 48 kHz stereo microphone bus.
/// RNNoise itself consumes 480-sample mono frames. This adapter downmixes the
/// microphone to mono, keeps the dry and processed paths time-aligned, and
/// introduces one fixed RNNoise frame of latency (about 10 ms).
/// </summary>
public sealed class RnNoiseSuppressionProcessor : IDisposable
{
    private const int Channels = 2;
    private const int FrameSize = 480;
    private const int QueueCapacity = FrameSize * 4;

    private readonly float[] _inputFrame = new float[FrameSize];
    private readonly float[] _processedFrame = new float[FrameSize];
    private readonly float[] _dryQueue = new float[QueueCapacity];
    private readonly float[] _wetQueue = new float[QueueCapacity];

    private Denoiser? _denoiser;
    private int _inputCount;
    private int _queueRead;
    private int _queueWrite;
    private int _queueCount;
    private int _enabled;
    private int _resetRequested;
    private float _strength;
    private bool _disposed;

    public RnNoiseSuppressionProcessor(bool enabled, float strength)
    {
        _strength = Math.Clamp(strength, 0.0f, 1.0f);
        _enabled = enabled ? 1 : 0;
        TryCreateDenoiser();
    }

    public bool IsAvailable => _denoiser is not null;
    public string? InitializationError { get; private set; }
    public float VoiceProbability { get; private set; }

    public void UpdateSettings(bool enabled, float strength)
    {
        Volatile.Write(ref _strength, Math.Clamp(strength, 0.0f, 1.0f));
        var newEnabled = enabled ? 1 : 0;
        if (Interlocked.Exchange(ref _enabled, newEnabled) != newEnabled)
        {
            Interlocked.Exchange(ref _resetRequested, 1);
        }
    }

    public void ProcessInPlace(float[] stereoSamples)
    {
        if (_disposed || stereoSamples.Length < Channels)
        {
            return;
        }

        if (Interlocked.Exchange(ref _resetRequested, 0) != 0)
        {
            ResetStreamingState(recreateDenoiser: Volatile.Read(ref _enabled) != 0);
        }

        var strength = Volatile.Read(ref _strength);
        if (Volatile.Read(ref _enabled) == 0 || strength <= 0.0001f || _denoiser is null)
        {
            return;
        }

        try
        {
            for (var i = 0; i + 1 < stereoSamples.Length; i += Channels)
            {
                var mono = Math.Clamp((stereoSamples[i] + stereoSamples[i + 1]) * 0.5f, -1.0f, 1.0f);
                _inputFrame[_inputCount++] = mono;

                if (_inputCount == FrameSize)
                {
                    ProcessCompletedFrame();
                    _inputCount = 0;
                }

                // The first frame is silence while RNNoise fills its 10 ms block.
                // Afterwards dry and wet samples are dequeued from the same frame,
                // so intermediate strengths do not create comb filtering.
                var output = 0.0f;
                if (TryDequeue(out var delayedDry, out var delayedWet))
                {
                    output = delayedDry + (delayedWet - delayedDry) * strength;
                }

                output = Math.Clamp(output, -1.0f, 1.0f);
                stereoSamples[i] = output;
                stereoSamples[i + 1] = output;
            }
        }
        catch (Exception ex)
        {
            InitializationError = ex.Message;
            ResetStreamingState(recreateDenoiser: false);
        }
    }

    private void ProcessCompletedFrame()
    {
        if (_denoiser is null)
        {
            return;
        }

        Array.Copy(_inputFrame, _processedFrame, FrameSize);
        _denoiser.Denoise(_processedFrame.AsSpan(), finish: true);

        // Denoiser.Denoise does not currently expose RNNoise's VAD result. Keep
        // a lightweight output-energy estimate available for future UI work.
        double energy = 0.0;
        for (var i = 0; i < FrameSize; i++)
        {
            var wet = Math.Clamp(_processedFrame[i], -1.0f, 1.0f);
            energy += wet * wet;
            Enqueue(_inputFrame[i], wet);
        }

        VoiceProbability = (float)Math.Clamp(Math.Sqrt(energy / FrameSize) * 8.0, 0.0, 1.0);
    }

    private void Enqueue(float dry, float wet)
    {
        if (_queueCount == QueueCapacity)
        {
            _queueRead = (_queueRead + 1) % QueueCapacity;
            _queueCount--;
        }

        _dryQueue[_queueWrite] = dry;
        _wetQueue[_queueWrite] = wet;
        _queueWrite = (_queueWrite + 1) % QueueCapacity;
        _queueCount++;
    }

    private bool TryDequeue(out float dry, out float wet)
    {
        if (_queueCount == 0)
        {
            dry = 0.0f;
            wet = 0.0f;
            return false;
        }

        dry = _dryQueue[_queueRead];
        wet = _wetQueue[_queueRead];
        _queueRead = (_queueRead + 1) % QueueCapacity;
        _queueCount--;
        return true;
    }

    private void ResetStreamingState(bool recreateDenoiser)
    {
        _inputCount = 0;
        _queueRead = 0;
        _queueWrite = 0;
        _queueCount = 0;
        VoiceProbability = 0.0f;
        Array.Clear(_inputFrame);
        Array.Clear(_processedFrame);
        Array.Clear(_dryQueue);
        Array.Clear(_wetQueue);

        _denoiser?.Dispose();
        _denoiser = null;

        if (recreateDenoiser)
        {
            TryCreateDenoiser();
        }
    }

    private void TryCreateDenoiser()
    {
        try
        {
            _denoiser = new Denoiser();
            InitializationError = null;
        }
        catch (Exception ex)
        {
            _denoiser = null;
            InitializationError = ex.Message;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _denoiser?.Dispose();
        _denoiser = null;
    }
}
