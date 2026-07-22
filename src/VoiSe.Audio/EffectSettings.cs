namespace VoiSe.Audio;

public sealed class EffectSettings
{
    // VoiSee 12 voice cleanup. RNNoise runs before gate, compressor and voice effects.
    public bool NoiseSuppressionEnabled { get; set; } = false;
    public float NoiseSuppressionStrength { get; set; } = 0.70f;

    // Global microphone-only high-pass cleanup before RNNoise and voice effects.
    public bool RumbleFilterEnabled { get; set; } = false;
    public float RumbleFilterCutoffHz { get; set; } = 90.0f;

    public float InputGainDb { get; set; } = 0.0f;
    public bool GateEnabled { get; set; } = true;
    public float GateThresholdDb { get; set; } = -45.0f;
    public bool CompressorEnabled { get; set; } = true;
    public float CompressorThresholdDb { get; set; } = -18.0f;
    public float CompressorRatio { get; set; } = 3.0f;
    public float VoiceGainDb { get; set; } = 0.0f;

    // Gate 6.5 connected DSP controls. PitchSemitones is clamped in the UI/DSP path.
    public float PitchSemitones { get; set; } = 0.0f;
    public float FormantShiftSemitones { get; set; } = 0.0f;
    public float BassAmount { get; set; } = 0.0f;
    public float TrebleAmount { get; set; } = 0.0f;
    public float DistortionAmount { get; set; } = 0.0f;
    public float RobotAmount { get; set; } = 0.0f;
    public float TremoloAmount { get; set; } = 0.0f;
    public float EchoAmount { get; set; } = 0.0f;
    public float ReverbAmount { get; set; } = 0.0f;
    public float RadioAmount { get; set; } = 0.0f;
    public float BitCrusherAmount { get; set; } = 0.0f;
    public float AlienAmount { get; set; } = 0.0f;

    public bool LimiterEnabled { get; set; } = true;
    public float LimiterCeilingDb { get; set; } = -1.0f;

    // Master output gain for the final virtual microphone mix.
    public float VirtualOutputGain { get; set; } = 1.0f;

    // Voice monitoring is independent from SoundBoard monitoring.
    // 0 = do not hear own processed voice in headphones, 1 = full voice monitor.
    public float VoiceMonitorGain { get; set; } = 0.0f;
}
