namespace VoiSe.Audio;

public sealed class SimpleVoiceProcessor
{
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private const int EchoDelaySamples = SampleRate * Channels / 4;      // ~250 ms
    private const int ReverbDelaySamples = SampleRate * Channels / 18;   // ~55 ms
    private const int PitchBufferSamples = 8192;                          // ~170 ms per channel at 48 kHz
    private const int PitchMinDelaySamples = 256;                         // ~5 ms safety delay
    private const int PitchDepthSamples = 2048;                           // ~43 ms pitch-shift grain depth
    private const int FormantBandCount = 4;
    private const int ChorusBufferSamples = 4096;
    private const int FlangerBufferSamples = 1024;
    private const int VibratoBufferSamples = 2048;
    private const int DoublerBufferSamples = 4096;
    private const int GhostBufferSamples = 8192;
    private const int WobblyBufferSamples = 4096;
    private const int PhaserStageCount = 4;
    private const int GenderFormantBandCount = 4;
    private const int PossessedBufferSamples = 8192;
    private const int VocoderBandCount = 6;

    private readonly object _sync = new();
    private EffectSettings _settings;
    private float _gateThreshold;
    private float _compressorThreshold;
    private float _inputGain;
    private float _voiceGain;
    private float _limiterCeiling;
    private float _pitchSemitones;
    private float _formantShiftSemitones;
    private float _bassAmount;
    private float _trebleAmount;
    private float _distortionAmount;
    private float _robotAmount;
    private float _tremoloAmount;
    private float _echoAmount;
    private float _reverbAmount;
    private float _radioAmount;
    private float _bitCrusherAmount;
    private float _alienAmount;
    private float _chorusAmount;
    private float _flangerAmount;
    private float _phaserAmount;
    private float _vibratoAmount;
    private float _doublerAmount;
    private float _ringModAmount;
    private float _chipmunkAmount;
    private float _giantAmount;
    private float _ghostAmount;
    private float _stutterAmount;
    private float _wobblyAmount;
    private float _genderMaleAmount;
    private float _genderFemaleAmount;
    private float _possessedAmount;
    private float _megaphoneAmount;
    private float _helicopterAmount;
    private float _cyborgAmount;
    private float _brokenRadioAmount;
    private float _vocoderAmount;
    private VoiceEffectKind[] _effectOrder = Array.Empty<VoiceEffectKind>();

    private readonly float[] _bassLow = new float[Channels];
    private readonly float[] _trebleLow = new float[Channels];
    private readonly float[,] _formantZ1 = new float[Channels, FormantBandCount];
    private readonly float[,] _formantZ2 = new float[Channels, FormantBandCount];
    private readonly float[] _formantB0 = new float[FormantBandCount];
    private readonly float[] _formantB1 = new float[FormantBandCount];
    private readonly float[] _formantB2 = new float[FormantBandCount];
    private readonly float[] _formantA1 = new float[FormantBandCount];
    private readonly float[] _formantA2 = new float[FormantBandCount];
    private readonly float[] _formantWeights = { 0.75f, 0.95f, 0.70f, 0.45f };
    private readonly float[] _radioLow = new float[Channels];
    private readonly float[] _radioBand = new float[Channels];
    private readonly float[] _bitHeld = new float[Channels];
    private readonly int[] _bitHoldRemaining = new int[Channels];
    private readonly float[] _echoBuffer = new float[EchoDelaySamples];
    private readonly float[] _reverbBuffer = new float[ReverbDelaySamples];
    private readonly float[][] _pitchBuffers = { new float[PitchBufferSamples], new float[PitchBufferSamples] };
    private readonly int[] _pitchWriteIndex = new int[Channels];
    private readonly float[][] _chorusBuffers = { new float[ChorusBufferSamples], new float[ChorusBufferSamples] };
    private readonly int[] _chorusWriteIndex = new int[Channels];
    private readonly float[][] _flangerBuffers = { new float[FlangerBufferSamples], new float[FlangerBufferSamples] };
    private readonly int[] _flangerWriteIndex = new int[Channels];
    private readonly float[][] _vibratoBuffers = { new float[VibratoBufferSamples], new float[VibratoBufferSamples] };
    private readonly int[] _vibratoWriteIndex = new int[Channels];
    private readonly float[][] _doublerBuffers = { new float[DoublerBufferSamples], new float[DoublerBufferSamples] };
    private readonly int[] _doublerWriteIndex = new int[Channels];
    private readonly float[][] _chipmunkPitchBuffers = { new float[PitchBufferSamples], new float[PitchBufferSamples] };
    private readonly int[] _chipmunkPitchWriteIndex = new int[Channels];
    private readonly float[][] _giantPitchBuffers = { new float[PitchBufferSamples], new float[PitchBufferSamples] };
    private readonly int[] _giantPitchWriteIndex = new int[Channels];
    private readonly float[][] _ghostBuffers = { new float[GhostBufferSamples], new float[GhostBufferSamples] };
    private readonly int[] _ghostWriteIndex = new int[Channels];
    private readonly float[][] _wobblyBuffers = { new float[WobblyBufferSamples], new float[WobblyBufferSamples] };
    private readonly int[] _wobblyWriteIndex = new int[Channels];
    private readonly float[,] _phaserState = new float[Channels, PhaserStageCount];
    private readonly float[][] _genderMalePitchBuffers = { new float[PitchBufferSamples], new float[PitchBufferSamples] };
    private readonly int[] _genderMalePitchWriteIndex = new int[Channels];
    private readonly float[][] _genderFemalePitchBuffers = { new float[PitchBufferSamples], new float[PitchBufferSamples] };
    private readonly int[] _genderFemalePitchWriteIndex = new int[Channels];
    private readonly float[,] _genderMaleZ1 = new float[Channels, GenderFormantBandCount];
    private readonly float[,] _genderMaleZ2 = new float[Channels, GenderFormantBandCount];
    private readonly float[,] _genderFemaleZ1 = new float[Channels, GenderFormantBandCount];
    private readonly float[,] _genderFemaleZ2 = new float[Channels, GenderFormantBandCount];
    private readonly float[] _genderMaleB0 = new float[GenderFormantBandCount];
    private readonly float[] _genderMaleB1 = new float[GenderFormantBandCount];
    private readonly float[] _genderMaleB2 = new float[GenderFormantBandCount];
    private readonly float[] _genderMaleA1 = new float[GenderFormantBandCount];
    private readonly float[] _genderMaleA2 = new float[GenderFormantBandCount];
    private readonly float[] _genderFemaleB0 = new float[GenderFormantBandCount];
    private readonly float[] _genderFemaleB1 = new float[GenderFormantBandCount];
    private readonly float[] _genderFemaleB2 = new float[GenderFormantBandCount];
    private readonly float[] _genderFemaleA1 = new float[GenderFormantBandCount];
    private readonly float[] _genderFemaleA2 = new float[GenderFormantBandCount];
    private readonly float[][] _possessedPitchBuffers = { new float[PitchBufferSamples], new float[PitchBufferSamples] };
    private readonly int[] _possessedPitchWriteIndex = new int[Channels];
    private readonly float[][] _possessedDelayBuffers = { new float[PossessedBufferSamples], new float[PossessedBufferSamples] };
    private readonly int[] _possessedDelayWriteIndex = new int[Channels];
    private readonly float[] _megaphoneLow = new float[Channels];
    private readonly float[] _megaphoneBand = new float[Channels];
    private readonly float[] _brokenRadioLow = new float[Channels];
    private readonly float[] _brokenRadioBand = new float[Channels];
    private readonly float[,] _vocoderAnalysisZ1 = new float[Channels, VocoderBandCount];
    private readonly float[,] _vocoderAnalysisZ2 = new float[Channels, VocoderBandCount];
    private readonly float[,] _vocoderCarrierZ1 = new float[Channels, VocoderBandCount];
    private readonly float[,] _vocoderCarrierZ2 = new float[Channels, VocoderBandCount];
    private readonly float[,] _vocoderEnvelope = new float[Channels, VocoderBandCount];
    private readonly float[] _vocoderB0 = new float[VocoderBandCount];
    private readonly float[] _vocoderB1 = new float[VocoderBandCount];
    private readonly float[] _vocoderB2 = new float[VocoderBandCount];
    private readonly float[] _vocoderA1 = new float[VocoderBandCount];
    private readonly float[] _vocoderA2 = new float[VocoderBandCount];
    private int _echoIndex;
    private int _reverbIndex;
    private double _robotPhase;
    private double _tremoloPhase;
    private double _alienPhase;
    private double _pitchPhase;
    private double _chorusPhase;
    private double _flangerPhase;
    private double _phaserPhase;
    private double _vibratoPhase;
    private double _ringModPhase;
    private double _chipmunkPitchPhase;
    private double _giantPitchPhase;
    private double _ghostPhase;
    private double _stutterPhase;
    private double _wobblyPhase;
    private double _genderMalePitchPhase;
    private double _genderFemalePitchPhase;
    private double _possessedPitchPhase;
    private double _possessedPhase;
    private double _helicopterPhase;
    private double _cyborgPhase;
    private double _brokenRadioPhase;
    private double _vocoderCarrierPhase;
    private uint _brokenRadioNoiseState = 0x5A17C9E3u;
    private float _robotMod = 1.0f;
    private float _tremoloMod = 1.0f;
    private float _alienMod = 1.0f;

    public SimpleVoiceProcessor(EffectSettings settings)
    {
        _settings = settings;
        Recalculate(settings);
    }

    public void UpdateSettings(EffectSettings settings)
    {
        lock (_sync)
        {
            _settings = settings;
            Recalculate(settings);
        }
    }

    public void ProcessInPlace(Span<float> samples)
    {
        EffectSettings settings;
        VoiceEffectKind[] effectOrder;
        float gateThreshold;
        float compressorThreshold;
        float inputGain;
        float voiceGain;
        float limiterCeiling;
        float pitchSemitones;
        float formantShiftSemitones;
        float bassAmount;
        float trebleAmount;
        float distortionAmount;
        float robotAmount;
        float tremoloAmount;
        float echoAmount;
        float reverbAmount;
        float radioAmount;
        float bitCrusherAmount;
        float alienAmount;
        float chorusAmount;
        float flangerAmount;
        float phaserAmount;
        float vibratoAmount;
        float doublerAmount;
        float ringModAmount;
        float chipmunkAmount;
        float giantAmount;
        float ghostAmount;
        float stutterAmount;
        float wobblyAmount;
        float genderMaleAmount;
        float genderFemaleAmount;
        float possessedAmount;
        float megaphoneAmount;
        float helicopterAmount;
        float cyborgAmount;
        float brokenRadioAmount;
        float vocoderAmount;

        lock (_sync)
        {
            settings = _settings;
            effectOrder = _effectOrder;
            gateThreshold = _gateThreshold;
            compressorThreshold = _compressorThreshold;
            inputGain = _inputGain;
            voiceGain = _voiceGain;
            limiterCeiling = _limiterCeiling;
            pitchSemitones = _pitchSemitones;
            formantShiftSemitones = _formantShiftSemitones;
            bassAmount = _bassAmount;
            trebleAmount = _trebleAmount;
            distortionAmount = _distortionAmount;
            robotAmount = _robotAmount;
            tremoloAmount = _tremoloAmount;
            echoAmount = _echoAmount;
            reverbAmount = _reverbAmount;
            radioAmount = _radioAmount;
            bitCrusherAmount = _bitCrusherAmount;
            alienAmount = _alienAmount;
            chorusAmount = _chorusAmount;
            flangerAmount = _flangerAmount;
            phaserAmount = _phaserAmount;
            vibratoAmount = _vibratoAmount;
            doublerAmount = _doublerAmount;
            ringModAmount = _ringModAmount;
            chipmunkAmount = _chipmunkAmount;
            giantAmount = _giantAmount;
            ghostAmount = _ghostAmount;
            stutterAmount = _stutterAmount;
            wobblyAmount = _wobblyAmount;
            genderMaleAmount = _genderMaleAmount;
            genderFemaleAmount = _genderFemaleAmount;
            possessedAmount = _possessedAmount;
            megaphoneAmount = _megaphoneAmount;
            helicopterAmount = _helicopterAmount;
            cyborgAmount = _cyborgAmount;
            brokenRadioAmount = _brokenRadioAmount;
            vocoderAmount = _vocoderAmount;
        }

        var bassGain = Decibels.DbToLinear(bassAmount * 10.0f);
        var trebleGain = Decibels.DbToLinear(trebleAmount * 10.0f);
        var bassMix = Math.Clamp(Math.Abs(bassAmount), 0.0f, 1.0f);
        var trebleMix = Math.Clamp(Math.Abs(trebleAmount), 0.0f, 1.0f);
        var distortionMix = Math.Clamp(Math.Max(0.0f, distortionAmount), 0.0f, 1.0f);
        var distortionDrive = 1.0f + distortionMix * 18.0f;
        var robotMix = Math.Clamp(Math.Max(0.0f, robotAmount), 0.0f, 1.0f);
        var tremoloDepth = Math.Clamp(Math.Max(0.0f, tremoloAmount), 0.0f, 1.0f) * 0.85f;
        var echoMix = Math.Clamp(Math.Max(0.0f, echoAmount), 0.0f, 1.0f) * 0.45f;
        var echoFeedback = Math.Clamp(Math.Max(0.0f, echoAmount), 0.0f, 1.0f) * 0.38f;
        var reverbMix = Math.Clamp(Math.Max(0.0f, reverbAmount), 0.0f, 1.0f) * 0.35f;
        var reverbFeedback = Math.Clamp(Math.Max(0.0f, reverbAmount), 0.0f, 1.0f) * 0.55f;
        var radioMix = Math.Clamp(Math.Max(0.0f, radioAmount), 0.0f, 1.0f);
        var bitMix = Math.Clamp(Math.Max(0.0f, bitCrusherAmount), 0.0f, 1.0f);
        var alienMix = Math.Clamp(Math.Max(0.0f, alienAmount), 0.0f, 1.0f);
        var chorusMix = Math.Clamp(Math.Max(0.0f, chorusAmount), 0.0f, 1.0f);
        var flangerMix = Math.Clamp(Math.Max(0.0f, flangerAmount), 0.0f, 1.0f);
        var phaserMix = Math.Clamp(Math.Max(0.0f, phaserAmount), 0.0f, 1.0f);
        var vibratoMix = Math.Clamp(Math.Max(0.0f, vibratoAmount), 0.0f, 1.0f);
        var doublerMix = Math.Clamp(Math.Max(0.0f, doublerAmount), 0.0f, 1.0f);
        var ringModMix = Math.Clamp(Math.Max(0.0f, ringModAmount), 0.0f, 1.0f);
        var chipmunkMix = Math.Clamp(Math.Max(0.0f, chipmunkAmount), 0.0f, 1.0f);
        var giantMix = Math.Clamp(Math.Max(0.0f, giantAmount), 0.0f, 1.0f);
        var ghostMix = Math.Clamp(Math.Max(0.0f, ghostAmount), 0.0f, 1.0f);
        var stutterMix = Math.Clamp(Math.Max(0.0f, stutterAmount), 0.0f, 1.0f);
        var wobblyMix = Math.Clamp(Math.Max(0.0f, wobblyAmount), 0.0f, 1.0f);
        var genderMaleMix = Math.Clamp(Math.Max(0.0f, genderMaleAmount), 0.0f, 1.0f);
        var genderFemaleMix = Math.Clamp(Math.Max(0.0f, genderFemaleAmount), 0.0f, 1.0f);
        var possessedMix = Math.Clamp(Math.Max(0.0f, possessedAmount), 0.0f, 1.0f);
        var megaphoneMix = Math.Clamp(Math.Max(0.0f, megaphoneAmount), 0.0f, 1.0f);
        var helicopterMix = Math.Clamp(Math.Max(0.0f, helicopterAmount), 0.0f, 1.0f);
        var cyborgMix = Math.Clamp(Math.Max(0.0f, cyborgAmount), 0.0f, 1.0f);
        var brokenRadioMix = Math.Clamp(Math.Max(0.0f, brokenRadioAmount), 0.0f, 1.0f);
        var vocoderMix = Math.Clamp(Math.Max(0.0f, vocoderAmount), 0.0f, 1.0f);
        var alienFrequency = 35.0f + alienMix * 180.0f;
        var bitDepth = (int)Math.Round(16 - bitMix * 12);
        bitDepth = Math.Clamp(bitDepth, 4, 16);
        var bitLevels = (1 << bitDepth) - 1;
        var bitHoldSamples = Math.Clamp((int)Math.Round(1 + bitMix * 18), 1, 24);

        for (var i = 0; i < samples.Length; i++)
        {
            var channel = i % Channels;
            if (channel == 0)
            {
                AdvanceModulators(robotMix, tremoloDepth, alienMix, alienFrequency, chorusMix, flangerMix, phaserMix, vibratoMix, ringModMix, ghostMix, stutterMix, wobblyMix, possessedMix, helicopterMix, cyborgMix, brokenRadioMix, vocoderMix);
            }

            var sample = samples[i] * inputGain;
            foreach (var effect in effectOrder)
            {
                switch (effect)
                {
                    case VoiceEffectKind.VoiceGain:
                        sample *= voiceGain;
                        break;
                    case VoiceEffectKind.Gate:
                        if (settings.GateEnabled && Math.Abs(sample) < gateThreshold)
                        {
                            sample = 0.0f;
                        }
                        break;
                    case VoiceEffectKind.Compressor:
                        if (settings.CompressorEnabled)
                        {
                            sample = CompressSample(sample, compressorThreshold, settings.CompressorRatio);
                        }
                        break;
                    case VoiceEffectKind.Pitch:
                        sample = ApplyPitchShift(sample, channel, pitchSemitones);
                        break;
                    case VoiceEffectKind.Formant:
                        sample = ApplyFormantShift(sample, channel, formantShiftSemitones);
                        break;
                    case VoiceEffectKind.Bass:
                        sample = ApplyBass(sample, channel, bassGain, bassMix);
                        break;
                    case VoiceEffectKind.Treble:
                        sample = ApplyTreble(sample, channel, trebleGain, trebleMix);
                        break;
                    case VoiceEffectKind.Chorus:
                        sample = ApplyChorus(sample, channel, chorusMix);
                        break;
                    case VoiceEffectKind.Flanger:
                        sample = ApplyFlanger(sample, channel, flangerMix);
                        break;
                    case VoiceEffectKind.Phaser:
                        sample = ApplyPhaser(sample, channel, phaserMix);
                        break;
                    case VoiceEffectKind.Vibrato:
                        sample = ApplyVibrato(sample, channel, vibratoMix);
                        break;
                    case VoiceEffectKind.Doubler:
                        sample = ApplyDoubler(sample, channel, doublerMix);
                        break;
                    case VoiceEffectKind.RingMod:
                        sample = ApplyRingMod(sample, ringModMix);
                        break;
                    case VoiceEffectKind.Chipmunk:
                        sample = ApplyComicPitch(sample, channel, chipmunkMix * 14.0f, chipmunkMix, _chipmunkPitchBuffers, _chipmunkPitchWriteIndex, ref _chipmunkPitchPhase);
                        break;
                    case VoiceEffectKind.Giant:
                        sample = ApplyComicPitch(sample, channel, giantMix * -12.0f, giantMix, _giantPitchBuffers, _giantPitchWriteIndex, ref _giantPitchPhase);
                        sample = Lerp(sample, MathF.Tanh(sample * 1.8f) * 0.82f, giantMix * 0.25f);
                        break;
                    case VoiceEffectKind.Ghost:
                        sample = ApplyGhost(sample, channel, ghostMix);
                        break;
                    case VoiceEffectKind.Stutter:
                        sample = ApplyStutter(sample, stutterMix);
                        break;
                    case VoiceEffectKind.Wobbly:
                        sample = ApplyWobbly(sample, channel, wobblyMix);
                        break;
                    case VoiceEffectKind.GenderMale:
                        sample = ApplyGenderMale(sample, channel, genderMaleMix);
                        break;
                    case VoiceEffectKind.GenderFemale:
                        sample = ApplyGenderFemale(sample, channel, genderFemaleMix);
                        break;
                    case VoiceEffectKind.Possessed:
                        sample = ApplyPossessed(sample, channel, possessedMix);
                        break;
                    case VoiceEffectKind.Megaphone:
                        sample = ApplyMegaphone(sample, channel, megaphoneMix);
                        break;
                    case VoiceEffectKind.Helicopter:
                        sample = ApplyHelicopter(sample, helicopterMix);
                        break;
                    case VoiceEffectKind.Cyborg:
                        sample = ApplyCyborg(sample, cyborgMix);
                        break;
                    case VoiceEffectKind.BrokenRadio:
                        sample = ApplyBrokenRadio(sample, channel, brokenRadioMix);
                        break;
                    case VoiceEffectKind.Vocoder:
                        sample = ApplyVocoder(sample, channel, vocoderMix);
                        break;
                    case VoiceEffectKind.Distortion:
                        sample = ApplyDistortion(sample, distortionMix, distortionDrive);
                        break;
                    case VoiceEffectKind.Robot:
                        sample = ApplyRobot(sample, robotMix);
                        break;
                    case VoiceEffectKind.Tremolo:
                        sample = ApplyTremolo(sample, tremoloDepth);
                        break;
                    case VoiceEffectKind.Echo:
                        sample = ApplyEcho(sample, i, echoMix, echoFeedback);
                        break;
                    case VoiceEffectKind.Reverb:
                        sample = ApplyReverb(sample, i, reverbMix, reverbFeedback);
                        break;
                    case VoiceEffectKind.Radio:
                        sample = ApplyRadio(sample, channel, radioMix);
                        break;
                    case VoiceEffectKind.BitCrusher:
                        sample = ApplyBitCrusher(sample, channel, bitMix, bitLevels, bitHoldSamples);
                        break;
                    case VoiceEffectKind.Alien:
                        sample = ApplyAlien(sample, alienMix);
                        break;
                }
            }

            if (settings.LimiterEnabled)
            {
                sample = Math.Clamp(sample, -limiterCeiling, limiterCeiling);
            }
            else
            {
                sample = Math.Clamp(sample, -1.0f, 1.0f);
            }

            samples[i] = sample;
        }
    }

    private void Recalculate(EffectSettings settings)
    {
        _gateThreshold = Decibels.DbToLinear(settings.GateThresholdDb);
        _compressorThreshold = Decibels.DbToLinear(settings.CompressorThresholdDb);
        _inputGain = Decibels.DbToLinear(settings.InputGainDb);
        _voiceGain = Decibels.DbToLinear(settings.VoiceGainDb);
        _limiterCeiling = Decibels.DbToLinear(settings.LimiterCeilingDb);
        _pitchSemitones = Math.Clamp(settings.PitchSemitones, -24.0f, 24.0f);
        _formantShiftSemitones = Math.Clamp(settings.FormantShiftSemitones, -24.0f, 24.0f);
        UpdateFormantCoefficients(_formantShiftSemitones);
        _bassAmount = ClampEffectAmount(settings.BassAmount);
        _trebleAmount = ClampEffectAmount(settings.TrebleAmount);
        _distortionAmount = ClampEffectAmount(settings.DistortionAmount);
        _robotAmount = ClampEffectAmount(settings.RobotAmount);
        _tremoloAmount = ClampEffectAmount(settings.TremoloAmount);
        _echoAmount = ClampEffectAmount(settings.EchoAmount);
        _reverbAmount = ClampEffectAmount(settings.ReverbAmount);
        _radioAmount = ClampEffectAmount(settings.RadioAmount);
        _bitCrusherAmount = ClampEffectAmount(settings.BitCrusherAmount);
        _alienAmount = ClampEffectAmount(settings.AlienAmount);
        _chorusAmount = ClampEffectAmount(settings.ChorusAmount);
        _flangerAmount = ClampEffectAmount(settings.FlangerAmount);
        _phaserAmount = ClampEffectAmount(settings.PhaserAmount);
        _vibratoAmount = ClampEffectAmount(settings.VibratoAmount);
        _doublerAmount = ClampEffectAmount(settings.DoublerAmount);
        _ringModAmount = ClampEffectAmount(settings.RingModAmount);
        _chipmunkAmount = ClampEffectAmount(settings.ChipmunkAmount);
        _giantAmount = ClampEffectAmount(settings.GiantAmount);
        _ghostAmount = ClampEffectAmount(settings.GhostAmount);
        _stutterAmount = ClampEffectAmount(settings.StutterAmount);
        _wobblyAmount = ClampEffectAmount(settings.WobblyAmount);
        _genderMaleAmount = ClampEffectAmount(settings.GenderMaleAmount);
        _genderFemaleAmount = ClampEffectAmount(settings.GenderFemaleAmount);
        _possessedAmount = ClampEffectAmount(settings.PossessedAmount);
        _megaphoneAmount = ClampEffectAmount(settings.MegaphoneAmount);
        _helicopterAmount = ClampEffectAmount(settings.HelicopterAmount);
        _cyborgAmount = ClampEffectAmount(settings.CyborgAmount);
        _brokenRadioAmount = ClampEffectAmount(settings.BrokenRadioAmount);
        _vocoderAmount = ClampEffectAmount(settings.VocoderAmount);
        UpdateGenderFormantCoefficients(_genderMaleAmount, isFemale: false);
        UpdateGenderFormantCoefficients(_genderFemaleAmount, isFemale: true);
        UpdateVocoderCoefficients();
        _effectOrder = settings.EffectOrder?.ToArray() ?? Array.Empty<VoiceEffectKind>();
    }

    private static float ClampEffectAmount(float value) => Math.Clamp(value, -4.0f, 4.0f);

    private void AdvanceModulators(float robotMix, float tremoloDepth, float alienMix, float alienFrequency,
        float chorusMix, float flangerMix, float phaserMix, float vibratoMix, float ringModMix,
        float ghostMix, float stutterMix, float wobblyMix, float possessedMix, float helicopterMix,
        float cyborgMix, float brokenRadioMix, float vocoderMix)
    {
        if (robotMix > 0.001f)
        {
            _robotPhase += 2.0 * Math.PI * 72.0 / SampleRate;
            if (_robotPhase > Math.PI * 2.0) _robotPhase -= Math.PI * 2.0;
            _robotMod = (float)Math.Sin(_robotPhase);
        }
        else
        {
            _robotMod = 1.0f;
        }

        if (tremoloDepth > 0.001f)
        {
            _tremoloPhase += 2.0 * Math.PI * 7.0 / SampleRate;
            if (_tremoloPhase > Math.PI * 2.0) _tremoloPhase -= Math.PI * 2.0;
            _tremoloMod = 1.0f - tremoloDepth + tremoloDepth * (0.5f + 0.5f * (float)Math.Sin(_tremoloPhase));
        }
        else
        {
            _tremoloMod = 1.0f;
        }


        if (alienMix > 0.001f)
        {
            _alienPhase += 2.0 * Math.PI * alienFrequency / SampleRate;
            if (_alienPhase > Math.PI * 2.0) _alienPhase -= Math.PI * 2.0;
            _alienMod = (float)Math.Sin(_alienPhase);
        }
        else
        {
            _alienMod = 1.0f;
        }

        AdvanceLfo(ref _chorusPhase, 0.82, chorusMix);
        AdvanceLfo(ref _flangerPhase, 0.28, flangerMix);
        AdvanceLfo(ref _phaserPhase, 0.36, phaserMix);
        AdvanceLfo(ref _vibratoPhase, 5.2, vibratoMix);
        AdvanceLfo(ref _ringModPhase, 34.0 + ringModMix * 210.0, ringModMix);
        AdvanceLfo(ref _ghostPhase, 2.4 + ghostMix * 2.2, ghostMix);
        AdvanceLfo(ref _stutterPhase, 5.0 + stutterMix * 8.0, stutterMix);
        AdvanceLfo(ref _wobblyPhase, 1.4 + wobblyMix * 2.8, wobblyMix);
        AdvanceLfo(ref _possessedPhase, 24.0 + possessedMix * 28.0, possessedMix);
        AdvanceLfo(ref _helicopterPhase, 6.5 + helicopterMix * 17.0, helicopterMix);
        AdvanceLfo(ref _cyborgPhase, 48.0 + cyborgMix * 235.0, cyborgMix);
        AdvanceLfo(ref _brokenRadioPhase, 1.8 + brokenRadioMix * 3.2, brokenRadioMix);
        AdvanceLfo(ref _vocoderCarrierPhase, 82.0 + vocoderMix * 58.0, vocoderMix);
    }

    private static void AdvanceLfo(ref double phase, double frequency, float amount)
    {
        if (amount <= 0.001f)
        {
            return;
        }

        phase += 2.0 * Math.PI * frequency / SampleRate;
        if (phase >= Math.PI * 2.0)
        {
            phase -= Math.PI * 2.0;
        }
    }


    private float ApplyPitchShift(float sample, int channel, float semitones)
    {
        return ApplyPitchShiftWithState(sample, channel, semitones, _pitchBuffers, _pitchWriteIndex, ref _pitchPhase);
    }

    private float ApplyComicPitch(float sample, int channel, float semitones, float mix, float[][] buffers, int[] writeIndices, ref double phase)
    {
        if (mix <= 0.001f)
        {
            return ApplyPitchShiftWithState(sample, channel, 0.0f, buffers, writeIndices, ref phase);
        }

        var shifted = ApplyPitchShiftWithState(sample, channel, semitones, buffers, writeIndices, ref phase);
        return Lerp(sample, shifted, 0.35f + mix * 0.65f);
    }

    private static float ApplyPitchShiftWithState(float sample, int channel, float semitones, float[][] buffers, int[] writeIndices, ref double phase)
    {
        var buffer = buffers[channel];
        var writeIndex = writeIndices[channel];
        buffer[writeIndex] = sample;

        if (Math.Abs(semitones) <= 0.01f)
        {
            writeIndices[channel] = (writeIndex + 1) % buffer.Length;
            return sample;
        }

        var ratio = MathF.Pow(2.0f, semitones / 12.0f);
        if (channel == 0)
        {
            AdvancePitchPhase(ref phase, ratio);
        }

        var phaseA = (float)phase;
        var phaseB = phaseA + 0.5f;
        if (phaseB >= 1.0f) phaseB -= 1.0f;

        var tapA = ReadPitchTap(buffer, writeIndex, phaseA);
        var tapB = ReadPitchTap(buffer, writeIndex, phaseB);
        var fadeA = 0.5f - 0.5f * MathF.Cos(phaseA * MathF.PI * 2.0f);
        var shifted = tapA * fadeA + tapB * (1.0f - fadeA);

        writeIndices[channel] = (writeIndex + 1) % buffer.Length;
        return Math.Clamp(shifted, -1.0f, 1.0f);
    }

    private static void AdvancePitchPhase(ref double phase, float ratio)
    {
        // Variable-delay pitch shifter: changing the read delay slope shifts pitch
        // while the crossfaded second tap keeps the output length stable.
        phase += (1.0 - ratio) / PitchDepthSamples;
        while (phase < 0.0) phase += 1.0;
        while (phase >= 1.0) phase -= 1.0;
    }

    private static float ReadPitchTap(float[] buffer, int writeIndex, float phase)
    {
        var delay = PitchMinDelaySamples + phase * PitchDepthSamples;
        var readPosition = writeIndex - delay;
        while (readPosition < 0) readPosition += buffer.Length;
        while (readPosition >= buffer.Length) readPosition -= buffer.Length;

        var index0 = (int)MathF.Floor(readPosition);
        var index1 = index0 + 1;
        if (index1 >= buffer.Length) index1 = 0;

        var frac = readPosition - index0;
        return buffer[index0] * (1.0f - frac) + buffer[index1] * frac;
    }


    private void UpdateFormantCoefficients(float semitones)
    {
        // A compact vocal-tract model: four band-pass resonators approximate
        // voice formant areas. The slider moves these resonances independently
        // from the pitch shifter, so it is audibly different from Bass/Treble.
        ReadOnlySpan<float> baseFrequencies = stackalloc float[] { 520.0f, 1450.0f, 2450.0f, 3400.0f };
        ReadOnlySpan<float> qValues = stackalloc float[] { 4.2f, 5.2f, 5.0f, 4.4f };
        var shift = MathF.Pow(2.0f, semitones / 12.0f);

        for (var band = 0; band < FormantBandCount; band++)
        {
            var frequency = Math.Clamp(baseFrequencies[band] * shift, 90.0f, SampleRate * 0.45f);
            var q = qValues[band];
            var omega = 2.0f * MathF.PI * frequency / SampleRate;
            var sin = MathF.Sin(omega);
            var cos = MathF.Cos(omega);
            var alpha = sin / (2.0f * q);
            var a0 = 1.0f + alpha;

            _formantB0[band] = alpha / a0;
            _formantB1[band] = 0.0f;
            _formantB2[band] = -alpha / a0;
            _formantA1[band] = -2.0f * cos / a0;
            _formantA2[band] = (1.0f - alpha) / a0;
        }
    }

    private float ApplyFormantShift(float sample, int channel, float semitones)
    {
        if (Math.Abs(semitones) <= 0.01f)
        {
            return sample;
        }

        var formantSum = 0.0f;
        var weightSum = 0.0f;
        for (var band = 0; band < FormantBandCount; band++)
        {
            var resonated = ProcessFormantBand(sample, channel, band);
            var weight = _formantWeights[band];
            formantSum += resonated * weight;
            weightSum += weight;
        }

        var model = weightSum > 0.001f ? formantSum / weightSum : sample;
        model = MathF.Tanh(model * 3.0f + sample * 0.35f);

        // At +/-100 the formant model is strong but not fully wet, which keeps
        // speech intelligible and avoids runaway resonances. Numeric fields can
        // push harder, but the amount is clamped for safety.
        var mix = Math.Clamp(Math.Abs(semitones) / 12.0f, 0.0f, 1.0f) * 0.82f;
        return Lerp(sample, model, mix);
    }

    private float ProcessFormantBand(float input, int channel, int band)
    {
        var output = _formantB0[band] * input + _formantZ1[channel, band];
        _formantZ1[channel, band] = _formantB1[band] * input - _formantA1[band] * output + _formantZ2[channel, band];
        _formantZ2[channel, band] = _formantB2[band] * input - _formantA2[band] * output;
        return output;
    }

    private float ApplyBass(float sample, int channel, float bassGain, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        const float alpha = 0.035f;
        _bassLow[channel] += alpha * (sample - _bassLow[channel]);
        var low = _bassLow[channel];
        var high = sample - low;
        var processed = low * bassGain + high;
        return Lerp(sample, processed, mix);
    }

    private float ApplyTreble(float sample, int channel, float trebleGain, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        const float alpha = 0.035f;
        _trebleLow[channel] += alpha * (sample - _trebleLow[channel]);
        var low = _trebleLow[channel];
        var high = sample - low;
        var processed = low + high * trebleGain;
        return Lerp(sample, processed, mix);
    }

    private float ApplyChorus(float sample, int channel, float mix)
    {
        var buffer = _chorusBuffers[channel];
        var writeIndex = _chorusWriteIndex[channel];
        buffer[writeIndex] = sample;

        if (mix <= 0.001f)
        {
            _chorusWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
            return sample;
        }

        var stereoPhase = _chorusPhase + (channel == 0 ? 0.0 : Math.PI * 0.55);
        var lfo = 0.5f + 0.5f * (float)Math.Sin(stereoPhase);
        var baseDelay = SampleRate * 0.016f;
        var depth = SampleRate * (0.0025f + 0.0065f * mix);
        var delayed = ReadDelayTap(buffer, writeIndex, baseDelay + depth * lfo);
        _chorusWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
        return Lerp(sample, delayed, 0.12f + mix * 0.48f);
    }

    private float ApplyFlanger(float sample, int channel, float mix)
    {
        var buffer = _flangerBuffers[channel];
        var writeIndex = _flangerWriteIndex[channel];

        if (mix <= 0.001f)
        {
            buffer[writeIndex] = sample;
            _flangerWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
            return sample;
        }

        var stereoPhase = _flangerPhase + (channel == 0 ? 0.0 : Math.PI);
        var lfo = 0.5f + 0.5f * (float)Math.Sin(stereoPhase);
        var delay = SampleRate * (0.0007f + (0.0010f + 0.0043f * mix) * lfo);
        var delayed = ReadDelayTap(buffer, writeIndex, delay);
        var feedback = 0.12f + mix * 0.43f;
        buffer[writeIndex] = Math.Clamp(sample + delayed * feedback, -1.0f, 1.0f);
        _flangerWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
        return Lerp(sample, delayed, 0.10f + mix * 0.55f);
    }

    private float ApplyPhaser(float sample, int channel, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        var lfo = 0.5f + 0.5f * (float)Math.Sin(_phaserPhase);
        var centerFrequency = 260.0f + lfo * (900.0f + 1_700.0f * mix);
        var processed = sample;
        for (var stage = 0; stage < PhaserStageCount; stage++)
        {
            var frequency = Math.Clamp(centerFrequency * (1.0f + stage * 0.34f), 90.0f, SampleRate * 0.42f);
            var tangent = MathF.Tan(MathF.PI * frequency / SampleRate);
            var coefficient = (tangent - 1.0f) / (tangent + 1.0f);
            var output = -coefficient * processed + _phaserState[channel, stage];
            _phaserState[channel, stage] = processed + coefficient * output;
            processed = output;
        }

        return Lerp(sample, processed, 0.15f + mix * 0.65f);
    }

    private float ApplyVibrato(float sample, int channel, float mix)
    {
        var buffer = _vibratoBuffers[channel];
        var writeIndex = _vibratoWriteIndex[channel];
        buffer[writeIndex] = sample;

        if (mix <= 0.001f)
        {
            _vibratoWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
            return sample;
        }

        var lfo = 0.5f + 0.5f * (float)Math.Sin(_vibratoPhase);
        var baseDelay = SampleRate * 0.0070f;
        var depth = SampleRate * (0.0008f + 0.0042f * mix);
        var delayed = ReadDelayTap(buffer, writeIndex, baseDelay + depth * lfo);
        _vibratoWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
        return Lerp(sample, delayed, 0.25f + mix * 0.75f);
    }

    private float ApplyDoubler(float sample, int channel, float mix)
    {
        var buffer = _doublerBuffers[channel];
        var writeIndex = _doublerWriteIndex[channel];
        buffer[writeIndex] = sample;

        if (mix <= 0.001f)
        {
            _doublerWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
            return sample;
        }

        // Slightly different delays per channel keep the copy from collapsing into a simple echo.
        var delaySeconds = channel == 0 ? 0.017f : 0.024f;
        var delayed = ReadDelayTap(buffer, writeIndex, SampleRate * delaySeconds);
        _doublerWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
        var wet = 0.10f + mix * 0.48f;
        return Math.Clamp(sample * (1.0f - wet * 0.30f) + delayed * wet, -1.0f, 1.0f);
    }

    private float ApplyRingMod(float sample, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        var carrier = (float)Math.Sin(_ringModPhase);
        return Lerp(sample, sample * carrier, mix);
    }

    private float ApplyGhost(float sample, int channel, float mix)
    {
        var buffer = _ghostBuffers[channel];
        var writeIndex = _ghostWriteIndex[channel];

        if (mix <= 0.001f)
        {
            buffer[writeIndex] = sample;
            _ghostWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
            return sample;
        }

        var shimmer = 0.56f + 0.44f * (float)Math.Sin(_ghostPhase + channel * 0.7);
        var delay = SampleRate * (0.045f + 0.085f * mix);
        var delayed = ReadDelayTap(buffer, writeIndex, delay);
        var feedback = 0.18f + 0.34f * mix;
        buffer[writeIndex] = Math.Clamp(sample * (0.78f + shimmer * 0.22f) + delayed * feedback, -1.0f, 1.0f);
        _ghostWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
        var spectral = delayed * (0.58f + shimmer * 0.22f) - sample * (0.10f * mix);
        return Lerp(sample, spectral, 0.30f + mix * 0.62f);
    }

    private float ApplyStutter(float sample, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        var normalizedPhase = (float)(_stutterPhase / (Math.PI * 2.0));
        var duty = 0.72f - mix * 0.52f;
        var gate = normalizedPhase <= duty ? 1.0f : 0.0f;
        return Lerp(sample, sample * gate, 0.45f + mix * 0.55f);
    }

    private float ApplyWobbly(float sample, int channel, float mix)
    {
        var buffer = _wobblyBuffers[channel];
        var writeIndex = _wobblyWriteIndex[channel];
        buffer[writeIndex] = sample;

        if (mix <= 0.001f)
        {
            _wobblyWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
            return sample;
        }

        var wobble = 0.62f * (float)Math.Sin(_wobblyPhase) + 0.38f * (float)Math.Sin(_wobblyPhase * 2.37 + 0.8);
        var baseDelay = SampleRate * 0.008f;
        var depth = SampleRate * (0.0015f + 0.0105f * mix);
        var delayed = ReadDelayTap(buffer, writeIndex, baseDelay + depth * (0.5f + 0.5f * wobble));
        _wobblyWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
        return Lerp(sample, delayed, 0.38f + mix * 0.62f);
    }

    private void UpdateGenderFormantCoefficients(float amount, bool isFemale)
    {
        var mix = Math.Clamp(Math.Max(0.0f, amount), 0.0f, 1.0f);
        var shiftSemitones = isFemale
            ? 1.5f + mix * 4.5f
            : -1.5f - mix * 4.5f;
        var shift = MathF.Pow(2.0f, shiftSemitones / 12.0f);
        ReadOnlySpan<float> baseFrequencies = stackalloc float[] { 520.0f, 1450.0f, 2450.0f, 3400.0f };
        ReadOnlySpan<float> qValues = stackalloc float[] { 4.0f, 5.0f, 4.8f, 4.2f };
        var b0 = isFemale ? _genderFemaleB0 : _genderMaleB0;
        var b1 = isFemale ? _genderFemaleB1 : _genderMaleB1;
        var b2 = isFemale ? _genderFemaleB2 : _genderMaleB2;
        var a1 = isFemale ? _genderFemaleA1 : _genderMaleA1;
        var a2 = isFemale ? _genderFemaleA2 : _genderMaleA2;

        for (var band = 0; band < GenderFormantBandCount; band++)
        {
            ConfigureBandPass(baseFrequencies[band] * shift, qValues[band], b0, b1, b2, a1, a2, band);
        }
    }

    private void UpdateVocoderCoefficients()
    {
        ReadOnlySpan<float> frequencies = stackalloc float[] { 220.0f, 430.0f, 820.0f, 1500.0f, 2700.0f, 4700.0f };
        ReadOnlySpan<float> qValues = stackalloc float[] { 1.35f, 1.45f, 1.55f, 1.65f, 1.75f, 1.85f };
        for (var band = 0; band < VocoderBandCount; band++)
        {
            ConfigureBandPass(frequencies[band], qValues[band], _vocoderB0, _vocoderB1, _vocoderB2, _vocoderA1, _vocoderA2, band);
        }
    }

    private static void ConfigureBandPass(float frequency, float q, float[] b0, float[] b1, float[] b2, float[] a1, float[] a2, int band)
    {
        frequency = Math.Clamp(frequency, 70.0f, SampleRate * 0.45f);
        q = Math.Clamp(q, 0.5f, 12.0f);
        var omega = 2.0f * MathF.PI * frequency / SampleRate;
        var sin = MathF.Sin(omega);
        var cos = MathF.Cos(omega);
        var alpha = sin / (2.0f * q);
        var a0 = 1.0f + alpha;
        b0[band] = alpha / a0;
        b1[band] = 0.0f;
        b2[band] = -alpha / a0;
        a1[band] = -2.0f * cos / a0;
        a2[band] = (1.0f - alpha) / a0;
    }

    private static float ProcessBiquadBand(float input, int channel, int band,
        float[] b0, float[] b1, float[] b2, float[] a1, float[] a2, float[,] z1, float[,] z2)
    {
        var output = b0[band] * input + z1[channel, band];
        z1[channel, band] = b1[band] * input - a1[band] * output + z2[channel, band];
        z2[channel, band] = b2[band] * input - a2[band] * output;
        return output;
    }

    private float ApplyGenderMale(float sample, int channel, float mix)
    {
        var semitones = -(1.5f + mix * 3.5f);
        var shifted = ApplyPitchShiftWithState(sample, channel, semitones, _genderMalePitchBuffers, _genderMalePitchWriteIndex, ref _genderMalePitchPhase);
        if (mix <= 0.001f)
        {
            return sample;
        }

        var resonant = 0.0f;
        for (var band = 0; band < GenderFormantBandCount; band++)
        {
            resonant += ProcessBiquadBand(shifted, channel, band, _genderMaleB0, _genderMaleB1, _genderMaleB2, _genderMaleA1, _genderMaleA2, _genderMaleZ1, _genderMaleZ2)
                * _formantWeights[band];
        }

        var body = MathF.Tanh(shifted * (1.45f + mix * 0.45f) + resonant * (1.15f + mix * 0.85f));
        return Lerp(sample, body * 0.90f, 0.35f + mix * 0.65f);
    }

    private float ApplyGenderFemale(float sample, int channel, float mix)
    {
        var semitones = 1.0f + mix * 3.0f;
        var shifted = ApplyPitchShiftWithState(sample, channel, semitones, _genderFemalePitchBuffers, _genderFemalePitchWriteIndex, ref _genderFemalePitchPhase);
        if (mix <= 0.001f)
        {
            return sample;
        }

        var resonant = 0.0f;
        for (var band = 0; band < GenderFormantBandCount; band++)
        {
            resonant += ProcessBiquadBand(shifted, channel, band, _genderFemaleB0, _genderFemaleB1, _genderFemaleB2, _genderFemaleA1, _genderFemaleA2, _genderFemaleZ1, _genderFemaleZ2)
                * _formantWeights[band];
        }

        var presence = MathF.Tanh(shifted * (1.15f + mix * 0.25f) + resonant * (1.35f + mix * 1.05f));
        return Lerp(sample, presence * 0.88f, 0.35f + mix * 0.65f);
    }

    private float ApplyPossessed(float sample, int channel, float mix)
    {
        var lowered = ApplyPitchShiftWithState(sample, channel, -(6.0f + mix * 6.0f), _possessedPitchBuffers, _possessedPitchWriteIndex, ref _possessedPitchPhase);
        var buffer = _possessedDelayBuffers[channel];
        var writeIndex = _possessedDelayWriteIndex[channel];
        buffer[writeIndex] = lowered;

        if (mix <= 0.001f)
        {
            _possessedDelayWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
            return sample;
        }

        var delayed = ReadDelayTap(buffer, writeIndex, SampleRate * (0.020f + mix * 0.040f));
        _possessedDelayWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
        var carrier = 0.76f + 0.24f * (float)Math.Sin(_possessedPhase + channel * 0.45);
        var layer = delayed * carrier;
        var possessed = MathF.Tanh(sample * (1.05f - mix * 0.20f) + layer * (0.85f + mix * 0.95f));
        return Lerp(sample, possessed * 0.92f, 0.35f + mix * 0.62f);
    }

    private float ApplyMegaphone(float sample, int channel, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        _megaphoneLow[channel] += 0.020f * (sample - _megaphoneLow[channel]);
        var highPassed = sample - _megaphoneLow[channel];
        _megaphoneBand[channel] += (0.16f + mix * 0.08f) * (highPassed - _megaphoneBand[channel]);
        var band = _megaphoneBand[channel];
        var driven = MathF.Tanh(band * (3.5f + mix * 8.5f));
        var compressed = CompressSample(driven, 0.22f, 4.0f + mix * 8.0f);
        var nasal = Math.Clamp(compressed * 1.28f, -1.0f, 1.0f);
        return Lerp(sample, nasal, 0.42f + mix * 0.58f);
    }

    private float ApplyHelicopter(float sample, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        var sine = 0.5f + 0.5f * (float)Math.Sin(_helicopterPhase);
        var rotor = sine * sine;
        rotor *= rotor;
        var blade = 0.08f + 0.92f * rotor;
        var flutter = 0.86f + 0.14f * (float)Math.Sin(_helicopterPhase * 5.0 + 0.7);
        var depth = 0.50f + mix * 0.48f;
        var chopped = sample * ((1.0f - depth) + depth * blade) * flutter;
        return Lerp(sample, chopped, 0.42f + mix * 0.58f);
    }

    private float ApplyCyborg(float sample, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        var carrier = (float)Math.Sin(_cyborgPhase);
        var harmonic = (float)Math.Sin(_cyborgPhase * 2.0 + 0.35);
        var metallic = MathF.Tanh((sample * 0.55f + sample * carrier * (0.75f + mix * 0.55f) + sample * harmonic * 0.22f) * (1.5f + mix * 2.4f));
        var levels = Math.Max(12, (int)Math.Round(72 - mix * 56));
        var normalized = Math.Clamp(metallic * 0.5f + 0.5f, 0.0f, 1.0f);
        var quantized = (MathF.Round(normalized * levels) / levels) * 2.0f - 1.0f;
        return Lerp(sample, quantized * 0.92f, 0.38f + mix * 0.62f);
    }

    private float ApplyBrokenRadio(float sample, int channel, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        _brokenRadioLow[channel] += 0.025f * (sample - _brokenRadioLow[channel]);
        var highPassed = sample - _brokenRadioLow[channel];
        _brokenRadioBand[channel] += 0.19f * (highPassed - _brokenRadioBand[channel]);

        _brokenRadioNoiseState = unchecked(_brokenRadioNoiseState * 1664525u + 1013904223u);
        var noise = (((_brokenRadioNoiseState >> 8) & 0xFFFFu) / 32767.5f) - 1.0f;
        var phase = (float)(_brokenRadioPhase / (Math.PI * 2.0));
        var dropout = phase > (0.72f - mix * 0.16f) && phase < (0.83f + mix * 0.08f) ? 0.12f : 1.0f;
        var crackle = Math.Abs(noise) > (0.965f - mix * 0.025f) ? noise * (0.18f + mix * 0.22f) : noise * 0.018f * mix;
        var dirty = MathF.Tanh(_brokenRadioBand[channel] * (2.8f + mix * 3.2f)) * dropout + crackle;
        var levels = Math.Max(10, (int)Math.Round(46 - mix * 32));
        var normalized = Math.Clamp(dirty * 0.5f + 0.5f, 0.0f, 1.0f);
        var quantized = (MathF.Round(normalized * levels) / levels) * 2.0f - 1.0f;
        return Lerp(sample, Math.Clamp(quantized, -1.0f, 1.0f), 0.45f + mix * 0.55f);
    }

    private float ApplyVocoder(float sample, int channel, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        var p = (float)_vocoderCarrierPhase;
        var carrier = 0.58f * MathF.Sin(p)
            + 0.25f * MathF.Sin(p * 2.0f)
            + 0.12f * MathF.Sin(p * 3.0f)
            + 0.07f * MathF.Sin(p * 4.0f);
        var vocoded = 0.0f;
        var envelopeTotal = 0.0f;

        for (var band = 0; band < VocoderBandCount; band++)
        {
            var analysis = ProcessBiquadBand(sample, channel, band, _vocoderB0, _vocoderB1, _vocoderB2, _vocoderA1, _vocoderA2, _vocoderAnalysisZ1, _vocoderAnalysisZ2);
            var target = Math.Abs(analysis);
            var envelope = _vocoderEnvelope[channel, band];
            var coefficient = target > envelope ? 0.075f : 0.0035f;
            envelope += coefficient * (target - envelope);
            _vocoderEnvelope[channel, band] = envelope;

            var carrierBand = ProcessBiquadBand(carrier, channel, band, _vocoderB0, _vocoderB1, _vocoderB2, _vocoderA1, _vocoderA2, _vocoderCarrierZ1, _vocoderCarrierZ2);
            var bandEnvelope = Math.Min(1.4f, envelope * (5.5f + band * 0.65f));
            vocoded += carrierBand * bandEnvelope;
            envelopeTotal += bandEnvelope;
        }

        if (envelopeTotal > 0.001f)
        {
            vocoded *= 2.4f / MathF.Sqrt(envelopeTotal);
        }

        var robotVoice = MathF.Tanh(vocoded * (1.7f + mix * 1.0f));
        return Lerp(sample, robotVoice * 0.95f, 0.38f + mix * 0.62f);
    }

    private static float ReadDelayTap(float[] buffer, int writeIndex, float delaySamples)
    {
        var readPosition = writeIndex - Math.Clamp(delaySamples, 1.0f, buffer.Length - 2.0f);
        while (readPosition < 0.0f) readPosition += buffer.Length;
        while (readPosition >= buffer.Length) readPosition -= buffer.Length;

        var index0 = (int)MathF.Floor(readPosition);
        var index1 = index0 + 1;
        if (index1 >= buffer.Length) index1 = 0;
        var fraction = readPosition - index0;
        return buffer[index0] * (1.0f - fraction) + buffer[index1] * fraction;
    }

    private float ApplyRadio(float sample, int channel, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        // Crude but audible radio/telephone band: remove lows, then low-pass the remaining signal.
        _radioLow[channel] += 0.018f * (sample - _radioLow[channel]);
        var highPassed = sample - _radioLow[channel];
        _radioBand[channel] += 0.22f * (highPassed - _radioBand[channel]);
        var radio = MathF.Tanh(_radioBand[channel] * 3.0f) * 0.75f;
        return Lerp(sample, radio, mix);
    }

    private float ApplyRobot(float sample, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        var robot = sample * _robotMod;
        return Lerp(sample, robot, mix);
    }

    private float ApplyAlien(float sample, float mix)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        var ring = sample * _alienMod;
        var folded = MathF.Tanh(ring * (1.0f + mix * 3.0f));
        return Lerp(sample, folded, mix * 0.85f);
    }

    private float ApplyTremolo(float sample, float depth)
    {
        return depth <= 0.001f ? sample : sample * _tremoloMod;
    }

    private static float ApplyDistortion(float sample, float mix, float drive)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        var distorted = MathF.Tanh(sample * drive) / MathF.Tanh(drive);
        return Lerp(sample, distorted, mix);
    }

    private float ApplyBitCrusher(float sample, int channel, float mix, int levels, int holdSamples)
    {
        if (mix <= 0.001f)
        {
            return sample;
        }

        if (_bitHoldRemaining[channel] <= 0)
        {
            var normalized = Math.Clamp(sample * 0.5f + 0.5f, 0.0f, 1.0f);
            var quantized = MathF.Round(normalized * levels) / levels;
            _bitHeld[channel] = quantized * 2.0f - 1.0f;
            _bitHoldRemaining[channel] = holdSamples;
        }

        _bitHoldRemaining[channel]--;
        return Lerp(sample, _bitHeld[channel], mix);
    }

    private float ApplyEcho(float sample, int index, float mix, float feedback)
    {
        var delayed = _echoBuffer[_echoIndex];
        _echoBuffer[_echoIndex] = Math.Clamp(sample + delayed * feedback, -1.0f, 1.0f);
        _echoIndex++;
        if (_echoIndex >= _echoBuffer.Length) _echoIndex = 0;

        return mix <= 0.001f ? sample : Math.Clamp(sample + delayed * mix, -1.0f, 1.0f);
    }

    private float ApplyReverb(float sample, int index, float mix, float feedback)
    {
        var delayed = _reverbBuffer[_reverbIndex];
        var input = sample + delayed * feedback;
        _reverbBuffer[_reverbIndex] = Math.Clamp(input, -1.0f, 1.0f);
        _reverbIndex++;
        if (_reverbIndex >= _reverbBuffer.Length) _reverbIndex = 0;

        return mix <= 0.001f ? sample : Math.Clamp(sample * (1.0f - mix) + delayed * mix, -1.0f, 1.0f);
    }

    private static float CompressSample(float sample, float compressorThreshold, float compressorRatio)
    {
        var abs = Math.Abs(sample);
        if (abs <= compressorThreshold)
        {
            return sample;
        }

        var sign = Math.Sign(sample);
        var excess = abs - compressorThreshold;
        var compressed = compressorThreshold + excess / Math.Max(1.0f, compressorRatio);
        return sign * compressed;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0.0f, 1.0f);
}
