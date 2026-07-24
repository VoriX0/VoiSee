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
        _effectOrder = settings.EffectOrder?.ToArray() ?? Array.Empty<VoiceEffectKind>();
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
