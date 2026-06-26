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

    private readonly object _sync = new();
    private EffectSettings _settings;
    private float _gateThreshold;
    private float _compressorThreshold;
    private float _inputGain;
    private float _voiceGain;
    private float _limiterCeiling;
    private float _pitchSemitones;
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

    private readonly float[] _toneLow = new float[Channels];
    private readonly float[] _radioLow = new float[Channels];
    private readonly float[] _radioBand = new float[Channels];
    private readonly float[] _bitHeld = new float[Channels];
    private readonly int[] _bitHoldRemaining = new int[Channels];
    private readonly float[] _echoBuffer = new float[EchoDelaySamples];
    private readonly float[] _reverbBuffer = new float[ReverbDelaySamples];
    private readonly float[][] _pitchBuffers = { new float[PitchBufferSamples], new float[PitchBufferSamples] };
    private readonly int[] _pitchWriteIndex = new int[Channels];
    private int _echoIndex;
    private int _reverbIndex;
    private double _robotPhase;
    private double _tremoloPhase;
    private double _alienPhase;
    private double _pitchPhase;
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
        float gateThreshold;
        float compressorThreshold;
        float inputGain;
        float voiceGain;
        float limiterCeiling;
        float pitchSemitones;
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

        lock (_sync)
        {
            settings = _settings;
            gateThreshold = _gateThreshold;
            compressorThreshold = _compressorThreshold;
            inputGain = _inputGain;
            voiceGain = _voiceGain;
            limiterCeiling = _limiterCeiling;
            pitchSemitones = _pitchSemitones;
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
        }

        var bassGain = Decibels.DbToLinear(bassAmount * 10.0f);
        var trebleGain = Decibels.DbToLinear(trebleAmount * 10.0f);
        var toneBlend = Math.Clamp(Math.Max(Math.Abs(bassAmount), Math.Abs(trebleAmount)), 0.0f, 1.0f);
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
                AdvanceModulators(robotMix, tremoloDepth, alienMix, alienFrequency);
            }

            var dry = samples[i] * inputGain;
            var sample = dry;

            if (settings.GateEnabled && Math.Abs(sample) < gateThreshold)
            {
                sample = 0.0f;
            }

            if (settings.CompressorEnabled)
            {
                sample = CompressSample(sample, compressorThreshold, settings.CompressorRatio);
            }

            sample = ApplyPitchShift(sample, channel, pitchSemitones);
            sample = ApplyTone(sample, channel, bassGain, trebleGain, toneBlend);
            sample = ApplyRadio(sample, channel, radioMix);
            sample = ApplyRobot(sample, robotMix);
            sample = ApplyAlien(sample, alienMix);
            sample = ApplyTremolo(sample, tremoloDepth);
            sample = ApplyDistortion(sample, distortionMix, distortionDrive);
            sample = ApplyBitCrusher(sample, channel, bitMix, bitLevels, bitHoldSamples);
            sample = ApplyEcho(sample, i, echoMix, echoFeedback);
            sample = ApplyReverb(sample, i, reverbMix, reverbFeedback);

            sample *= voiceGain;

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
    }

    private static float ClampEffectAmount(float value) => Math.Clamp(value, -4.0f, 4.0f);

    private void AdvanceModulators(float robotMix, float tremoloDepth, float alienMix, float alienFrequency)
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
    }


    private float ApplyPitchShift(float sample, int channel, float semitones)
    {
        var buffer = _pitchBuffers[channel];
        var writeIndex = _pitchWriteIndex[channel];
        buffer[writeIndex] = sample;

        if (Math.Abs(semitones) <= 0.01f)
        {
            _pitchWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
            return sample;
        }

        var ratio = MathF.Pow(2.0f, semitones / 12.0f);
        if (channel == 0)
        {
            AdvancePitchPhase(ratio);
        }

        var phaseA = (float)_pitchPhase;
        var phaseB = phaseA + 0.5f;
        if (phaseB >= 1.0f) phaseB -= 1.0f;

        var tapA = ReadPitchTap(buffer, writeIndex, phaseA);
        var tapB = ReadPitchTap(buffer, writeIndex, phaseB);

        // Cosine crossfade hides the discontinuity where each moving delay wraps.
        var fadeA = 0.5f - 0.5f * MathF.Cos(phaseA * MathF.PI * 2.0f);
        var shifted = tapA * fadeA + tapB * (1.0f - fadeA);

        _pitchWriteIndex[channel] = (writeIndex + 1) % buffer.Length;
        return Math.Clamp(shifted, -1.0f, 1.0f);
    }

    private void AdvancePitchPhase(float ratio)
    {
        // Variable-delay pitch shifter: changing the read delay slope shifts pitch
        // while the crossfaded second tap keeps the output length stable.
        _pitchPhase += (1.0 - ratio) / PitchDepthSamples;
        while (_pitchPhase < 0.0) _pitchPhase += 1.0;
        while (_pitchPhase >= 1.0) _pitchPhase -= 1.0;
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

    private float ApplyTone(float sample, int channel, float bassGain, float trebleGain, float toneBlend)
    {
        if (toneBlend <= 0.001f)
        {
            return sample;
        }

        const float alpha = 0.035f;
        _toneLow[channel] += alpha * (sample - _toneLow[channel]);
        var low = _toneLow[channel];
        var high = sample - low;
        var toned = low * bassGain + high * trebleGain;
        return Lerp(sample, toned, toneBlend);
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
