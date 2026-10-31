namespace VoiSe.Audio;

// Keeps the clean microphone path lightweight while preserving the original
// per-voice input gain and limiter, before mixing with SoundBoard/Media Bridge.
internal sealed class VoiceProcessingChain
{
    private SimpleVoiceProcessor? _processor;
    private float _inputGain, _ceiling;
    public bool IsFastPath => _processor is null;
    public VoiceProcessingChain(EffectSettings settings) => Update(settings);
    public void Update(EffectSettings settings)
    {
        _inputGain = Decibels.DbToLinear(settings.InputGainDb);
        _ceiling = settings.LimiterEnabled ? Decibels.DbToLinear(settings.LimiterCeilingDb) : 1f;
        if (settings.EffectOrder is not { Count: > 0 }) _processor = null;
        else if (_processor is null) _processor = new SimpleVoiceProcessor(settings);
        else _processor.UpdateSettings(settings);
    }
    public void Process(Span<float> samples)
    {
        if (_processor is not null) { _processor.ProcessInPlace(samples); return; }
        for (var i = 0; i < samples.Length; i++) samples[i] = Math.Clamp(samples[i] * _inputGain, -_ceiling, _ceiling);
    }
}
