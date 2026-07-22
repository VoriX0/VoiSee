using System.Threading;

namespace VoiSe.Audio;

/// <summary>
/// Low-latency first-order high-pass filter for microphone-only cleanup.
/// It reduces desk vibration, HVAC rumble and excessive plosive energy before
/// RNNoise, gate/compressor and the entertainment effect chain.
/// </summary>
public sealed class HighPassCleanupProcessor
{
    private const int SampleRate = 48_000;
    private const int Channels = 2;

    private readonly float[] _previousInput = new float[Channels];
    private readonly float[] _previousOutput = new float[Channels];
    private int _enabled;
    private int _resetRequested;
    private float _cutoffHz;

    public HighPassCleanupProcessor(bool enabled, float cutoffHz)
    {
        _enabled = enabled ? 1 : 0;
        _cutoffHz = ClampCutoff(cutoffHz);
    }

    public void UpdateSettings(bool enabled, float cutoffHz)
    {
        Volatile.Write(ref _cutoffHz, ClampCutoff(cutoffHz));
        var nextEnabled = enabled ? 1 : 0;
        if (Interlocked.Exchange(ref _enabled, nextEnabled) != nextEnabled)
        {
            Interlocked.Exchange(ref _resetRequested, 1);
        }
    }

    public void ProcessInPlace(Span<float> stereoSamples)
    {
        if (Interlocked.Exchange(ref _resetRequested, 0) != 0)
        {
            ResetState();
        }

        if (Volatile.Read(ref _enabled) == 0 || stereoSamples.Length < Channels)
        {
            return;
        }

        var cutoff = Volatile.Read(ref _cutoffHz);
        var dt = 1.0f / SampleRate;
        var rc = 1.0f / (2.0f * MathF.PI * cutoff);
        var alpha = rc / (rc + dt);

        for (var i = 0; i < stereoSamples.Length; i++)
        {
            var channel = i % Channels;
            var input = stereoSamples[i];
            var output = alpha * (_previousOutput[channel] + input - _previousInput[channel]);
            _previousInput[channel] = input;
            _previousOutput[channel] = output;
            stereoSamples[i] = Math.Clamp(output, -1.0f, 1.0f);
        }
    }

    private void ResetState()
    {
        Array.Clear(_previousInput);
        Array.Clear(_previousOutput);
    }

    private static float ClampCutoff(float cutoffHz) => Math.Clamp(cutoffHz, 50.0f, 160.0f);
}
