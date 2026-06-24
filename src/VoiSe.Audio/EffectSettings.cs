namespace VoiSe.Audio;

public sealed class EffectSettings
{
    public float InputGainDb { get; init; } = 0.0f;
    public bool GateEnabled { get; init; } = true;
    public float GateThresholdDb { get; init; } = -45.0f;
    public bool CompressorEnabled { get; init; } = true;
    public float CompressorThresholdDb { get; init; } = -18.0f;
    public float CompressorRatio { get; init; } = 3.0f;
    public float VoiceGainDb { get; init; } = 0.0f;
    public bool LimiterEnabled { get; init; } = true;
    public float LimiterCeilingDb { get; init; } = -1.0f;
}
