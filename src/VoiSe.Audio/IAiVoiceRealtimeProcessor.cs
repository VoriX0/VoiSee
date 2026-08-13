namespace VoiSe.Audio;

/// <summary>
/// Backend-neutral contract for realtime AI Voice processors. The audio engine
/// only knows how to feed/consume PCM and observe runtime state; RVC/zero-shot
/// implementation details stay behind this boundary.
/// </summary>
internal interface IAiVoiceRealtimeProcessor : IDisposable
{
    event Action<AiVoiceRuntimeStatus>? StatusChanged;
    AiVoiceRuntimeStatus GetStatus();
    double EstimatedStageLatencyMs { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    void ProcessInPlace(float[] stereoSamples);
}
