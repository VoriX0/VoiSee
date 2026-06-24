namespace VoiSe.Audio;

public sealed class SimpleVoiceProcessor
{
    private readonly EffectSettings _settings;
    private readonly float _gateThreshold;
    private readonly float _compressorThreshold;
    private readonly float _inputGain;
    private readonly float _voiceGain;
    private readonly float _limiterCeiling;

    public SimpleVoiceProcessor(EffectSettings settings)
    {
        _settings = settings;
        _gateThreshold = Decibels.DbToLinear(settings.GateThresholdDb);
        _compressorThreshold = Decibels.DbToLinear(settings.CompressorThresholdDb);
        _inputGain = Decibels.DbToLinear(settings.InputGainDb);
        _voiceGain = Decibels.DbToLinear(settings.VoiceGainDb);
        _limiterCeiling = Decibels.DbToLinear(settings.LimiterCeilingDb);
    }

    public void ProcessInPlace(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i] * _inputGain;

            if (_settings.GateEnabled && Math.Abs(sample) < _gateThreshold)
            {
                sample = 0.0f;
            }

            if (_settings.CompressorEnabled)
            {
                sample = CompressSample(sample);
            }

            sample *= _voiceGain;

            if (_settings.LimiterEnabled)
            {
                sample = Math.Clamp(sample, -_limiterCeiling, _limiterCeiling);
            }
            else
            {
                sample = Math.Clamp(sample, -1.0f, 1.0f);
            }

            samples[i] = sample;
        }
    }

    private float CompressSample(float sample)
    {
        var abs = Math.Abs(sample);
        if (abs <= _compressorThreshold)
        {
            return sample;
        }

        var sign = Math.Sign(sample);
        var excess = abs - _compressorThreshold;
        var compressed = _compressorThreshold + excess / Math.Max(1.0f, _settings.CompressorRatio);
        return sign * compressed;
    }
}
