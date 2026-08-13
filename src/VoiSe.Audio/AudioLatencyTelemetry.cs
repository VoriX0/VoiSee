namespace VoiSe.Audio;

public sealed record AudioLatencySnapshot(
    DateTimeOffset Timestamp,
    AudioProcessingPath ProcessingPath,
    double CaptureBufferMs,
    double FormatConversionProcessingMs,
    double NoiseSuppressionProcessingMs,
    double AiProcessingMs,
    double DspProcessingMs,
    double VirtualMicQueuedAudioMs,
    double MonitorQueuedAudioMs,
    double RenderBufferTargetMs,
    double EstimatedTotalMs,
    long VirtualMicDroppedSamples,
    long MonitorDroppedSamples);

/// <summary>
/// Lock-protected, allocation-light latency telemetry for the real-time audio path.
/// Processing durations use a small exponential moving average so the diagnostics
/// remain readable without hiding persistent spikes.
/// </summary>
internal sealed class AudioLatencyTelemetry
{
    private const double Smoothing = 0.15;
    private readonly object _sync = new();

    private DateTimeOffset _timestamp = DateTimeOffset.UtcNow;
    private AudioProcessingPath _processingPath = AudioProcessingPath.Fast;
    private double _captureBufferMs;
    private double _formatConversionMs;
    private double _noiseSuppressionMs;
    private double _aiProcessingMs;
    private double _dspProcessingMs;
    private double _virtualQueueMs;
    private double _monitorQueueMs;
    private double _renderBufferTargetMs;
    private long _virtualDroppedSamples;
    private long _monitorDroppedSamples;
    private bool _hasProcessingSample;

    public void Record(
        AudioProcessingPath processingPath,
        double captureBufferMs,
        double formatConversionMs,
        double noiseSuppressionMs,
        double aiProcessingMs,
        double dspProcessingMs,
        double virtualQueueMs,
        double monitorQueueMs,
        double renderBufferTargetMs,
        long virtualDroppedSamples,
        long monitorDroppedSamples)
    {
        lock (_sync)
        {
            var pathChanged = _processingPath != processingPath;
            _timestamp = DateTimeOffset.UtcNow;
            _processingPath = processingPath;
            _captureBufferMs = Math.Max(0.0, captureBufferMs);
            _virtualQueueMs = Math.Max(0.0, virtualQueueMs);
            _monitorQueueMs = Math.Max(0.0, monitorQueueMs);
            _renderBufferTargetMs = Math.Max(0.0, renderBufferTargetMs);
            _virtualDroppedSamples = Math.Max(0, virtualDroppedSamples);
            _monitorDroppedSamples = Math.Max(0, monitorDroppedSamples);

            if (!_hasProcessingSample || pathChanged)
            {
                _formatConversionMs = Math.Max(0.0, formatConversionMs);
                _noiseSuppressionMs = Math.Max(0.0, noiseSuppressionMs);
                _aiProcessingMs = Math.Max(0.0, aiProcessingMs);
                _dspProcessingMs = Math.Max(0.0, dspProcessingMs);
                _hasProcessingSample = true;
            }
            else
            {
                _formatConversionMs = Smooth(_formatConversionMs, formatConversionMs);
                _noiseSuppressionMs = Smooth(_noiseSuppressionMs, noiseSuppressionMs);
                _aiProcessingMs = Smooth(_aiProcessingMs, aiProcessingMs);
                _dspProcessingMs = Smooth(_dspProcessingMs, dspProcessingMs);
            }
        }
    }

    public AudioLatencySnapshot Snapshot()
    {
        lock (_sync)
        {
            var estimated = _captureBufferMs
                + _formatConversionMs
                + _noiseSuppressionMs
                + _aiProcessingMs
                + _dspProcessingMs
                + _virtualQueueMs
                + _renderBufferTargetMs;

            return new AudioLatencySnapshot(
                _timestamp,
                _processingPath,
                _captureBufferMs,
                _formatConversionMs,
                _noiseSuppressionMs,
                _aiProcessingMs,
                _dspProcessingMs,
                _virtualQueueMs,
                _monitorQueueMs,
                _renderBufferTargetMs,
                estimated,
                _virtualDroppedSamples,
                _monitorDroppedSamples);
        }
    }

    private static double Smooth(double previous, double current)
    {
        current = Math.Max(0.0, current);
        return previous + ((current - previous) * Smoothing);
    }
}
