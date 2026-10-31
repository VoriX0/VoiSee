namespace VoiSe.Audio;

internal static class AudioTiming
{
    public const int CaptureMs = 10;
    public const int RenderMs = 20;
    public const int MaxQueuedMs = 100;
    public static int QueueSamples(int sampleRate, int channels) => checked((int)((long)sampleRate * MaxQueuedMs / 1000) * channels);
}

// Internal measurements exclude device ADC/DAC, driver buffering, VB-CABLE,
// the receiving application and network. They are not end-to-end latency.
public readonly record struct AudioTimingSnapshot(bool FastPath, double ProcessingMs, double MicrophoneQueuedMs,
    double MediaQueuedMs, long DroppedMicrophoneSamples, int CaptureTargetMs, int RenderTargetMs);
