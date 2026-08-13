namespace VoiSe.Audio;

/// <summary>
/// Backend-neutral launch contract passed from the VoiSee app/model layer to
/// the audio engine. Backend-specific managers fill the generic runtime/asset
/// paths; the engine selects the matching processor implementation.
/// </summary>
public sealed record AiVoiceRuntimeConfiguration(
    AiVoiceRuntimeBackend Backend,
    string PythonExecutablePath,
    string BridgeScriptPath,
    string RuntimeRootPath,
    string PrimaryAssetPath,
    string? SecondaryAssetPath,
    AiPerformanceProfile PerformanceProfile,
    string VoiceId,
    string VoiceName);

public enum AiVoiceRuntimeBackend
{
    Rvc = 0,
    ZeroShot = 1,
}

public enum AiVoiceRuntimeState
{
    Disabled = 0,
    Starting = 1,
    Ready = 2,
    Faulted = 3,
}

public sealed record AiVoiceRuntimeStatus(
    AiVoiceRuntimeState State,
    string? VoiceId,
    string? VoiceName,
    string? Error,
    int BlockMs,
    double LastInferenceMs,
    double LastRoundTripMs,
    double InputQueuedMs,
    double OutputQueuedMs,
    long InputDroppedSamples,
    long OutputUnderrunSamples)
{
    public bool IsActive => State is AiVoiceRuntimeState.Starting or AiVoiceRuntimeState.Ready;

    public static AiVoiceRuntimeStatus Disabled { get; } = new(
        AiVoiceRuntimeState.Disabled,
        null,
        null,
        null,
        0,
        0.0,
        0.0,
        0.0,
        0.0,
        0,
        0);
}
