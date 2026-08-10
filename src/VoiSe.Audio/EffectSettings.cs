namespace VoiSe.Audio;

public sealed class EffectSettings
{
    // VoiSee 12.1 global microphone cleanup. The selected engine runs before
    // gate, compressor and the preset-controlled voice effects.
    public NoiseSuppressionMode NoiseSuppressionMode { get; set; } = NoiseSuppressionMode.Off;
    public float NoiseSuppressionStrength { get; set; } = 0.70f;

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
    public float ChorusAmount { get; set; } = 0.0f;
    public float FlangerAmount { get; set; } = 0.0f;
    public float PhaserAmount { get; set; } = 0.0f;
    public float VibratoAmount { get; set; } = 0.0f;
    public float DoublerAmount { get; set; } = 0.0f;
    public float RingModAmount { get; set; } = 0.0f;
    public float ChipmunkAmount { get; set; } = 0.0f;
    public float GiantAmount { get; set; } = 0.0f;
    public float GhostAmount { get; set; } = 0.0f;
    public float StutterAmount { get; set; } = 0.0f;
    public float WobblyAmount { get; set; } = 0.0f;
    public float GenderMaleAmount { get; set; } = 0.0f;
    public float GenderFemaleAmount { get; set; } = 0.0f;
    public float PossessedAmount { get; set; } = 0.0f;
    public float MegaphoneAmount { get; set; } = 0.0f;
    public float HelicopterAmount { get; set; } = 0.0f;
    public float CyborgAmount { get; set; } = 0.0f;
    public float BrokenRadioAmount { get; set; } = 0.0f;
    public float VocoderAmount { get; set; } = 0.0f;

    public bool LimiterEnabled { get; set; } = true;
    public float LimiterCeilingDb { get; set; } = -1.0f;

    // Master output gain for the final virtual microphone mix.
    public float VirtualOutputGain { get; set; } = 1.0f;

    // Voice monitoring is independent from SoundBoard monitoring.
    // 0 = do not hear own processed voice in headphones, 1 = full voice monitor.
    public float VoiceMonitorGain { get; set; } = 0.0f;

    // VoiSee 12.2 ordered card chain. The limiter remains a fixed final safety stage.
    public IReadOnlyList<VoiceEffectKind> EffectOrder { get; set; } = new[]
    {
        VoiceEffectKind.Gate,
        VoiceEffectKind.Compressor,
        VoiceEffectKind.Pitch,
        VoiceEffectKind.Formant,
        VoiceEffectKind.Bass,
        VoiceEffectKind.Treble,
        VoiceEffectKind.Chorus,
        VoiceEffectKind.Flanger,
        VoiceEffectKind.Phaser,
        VoiceEffectKind.Vibrato,
        VoiceEffectKind.Doubler,
        VoiceEffectKind.RingMod,
        VoiceEffectKind.Chipmunk,
        VoiceEffectKind.Giant,
        VoiceEffectKind.Ghost,
        VoiceEffectKind.Stutter,
        VoiceEffectKind.Wobbly,
        VoiceEffectKind.GenderMale,
        VoiceEffectKind.GenderFemale,
        VoiceEffectKind.Possessed,
        VoiceEffectKind.Megaphone,
        VoiceEffectKind.Helicopter,
        VoiceEffectKind.Cyborg,
        VoiceEffectKind.BrokenRadio,
        VoiceEffectKind.Vocoder,
        VoiceEffectKind.Radio,
        VoiceEffectKind.Robot,
        VoiceEffectKind.Alien,
        VoiceEffectKind.Tremolo,
        VoiceEffectKind.Distortion,
        VoiceEffectKind.BitCrusher,
        VoiceEffectKind.Echo,
        VoiceEffectKind.Reverb,
        VoiceEffectKind.VoiceGain
    };
}
