namespace VoiSe.Audio;

// Fixed 48k microphone correction, after denoising and before character DSP.
// No look-ahead, worker thread, file access, or per-block allocations.
internal sealed class MicrophoneEqualizer
{
    private readonly int _sampleRate, _channels, _fadeFrames;
    private Bank _current, _previous;
    private int _remainingFade;
    public MicrophoneEqualizer(int sampleRate, int channels, EqualizerSettings? settings = null)
    {
        if (sampleRate < 44100 || channels < 1) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _sampleRate = sampleRate; _channels = channels; _fadeFrames = sampleRate / 50;
        _current = _previous = new Bank(settings ?? new(), sampleRate, channels);
    }
    // Called under the engine's processing boundary, never from the capture callback.
    public void Update(EqualizerSettings settings)
    {
        var normalized = settings.NormalizedCopy();
        if (_current.Settings.Enabled == normalized.Enabled && _current.Settings.PreampDb == normalized.PreampDb
            && _current.Settings.GainsDb.AsSpan().SequenceEqual(normalized.GainsDb)
            && _current.Settings.ParametricBands.SequenceEqual(normalized.ParametricBands)) return;
        _previous = _current; _current = new Bank(normalized, _sampleRate, _channels);
        _remainingFade = _fadeFrames;
    }
    public void Process(Span<float> samples)
    {
        if (samples.Length % _channels != 0) throw new ArgumentException("Unaligned EQ block.");
        if (_current.IsFlat && _remainingFade == 0) return;
        for (var frame = 0; frame < samples.Length; frame += _channels)
        {
            var mix = 1.0 - _remainingFade / (double)_fadeFrames;
            for (var channel = 0; channel < _channels; channel++)
            {
                var dry = float.IsFinite(samples[frame + channel]) ? samples[frame + channel] : 0f;
                var output = _current.Process(dry, channel);
                if (_remainingFade > 0) output = _previous.Process(dry, channel) * (1 - mix) + output * mix;
                samples[frame + channel] = (float)Math.Clamp(output, -float.MaxValue, float.MaxValue);
            }
            if (_remainingFade > 0 && --_remainingFade == 0) _previous = _current;
        }
    }
    private sealed class Bank
    {
        public EqualizerSettings Settings { get; }
        private readonly List<Band> _bands = new();
        private readonly double _preamp;
        public bool IsFlat { get; }
        public Bank(EqualizerSettings settings, int rate, int channels)
        {
            Settings = settings.NormalizedCopy();
            if (!Settings.Enabled) { IsFlat = true; _preamp = 1; return; }
            _preamp = Math.Pow(10, Settings.PreampDb / 20);
            for (var i = 0; i < 31; i++)
                if (Math.Abs(Settings.GainsDb[i]) > .00001)
                    _bands.Add(new Band(EqualizerCoefficients.Create(EqualizerSettings.Frequencies[i], Settings.GainsDb[i], 4.318473046963146, rate), channels));
            foreach (var band in Settings.ParametricBands)
                if (band.Enabled && (Math.Abs(band.GainDb) > .00001 || band.Filter is EqualizerFilter.HighPass or EqualizerFilter.LowPass))
                    _bands.Add(new Band(EqualizerCoefficients.Create(band.Frequency, band.GainDb, band.Q, rate, band.Filter), channels));
            IsFlat = _bands.Count == 0 && Settings.PreampDb == 0;
        }
        public double Process(double sample, int channel)
        {
            if (IsFlat) return sample;
            sample *= _preamp;
            // Indexed access: no iterator allocation in the audio path.
            for (var i = 0; i < _bands.Count; i++) sample = _bands[i].Process(sample, channel);
            return sample;
        }
    }
    private sealed class Band
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private readonly double[] _z1, _z2;
        public Band(EqualizerCoefficients coefficients, int channels)
        {
            _b0 = coefficients.B0; _b1 = coefficients.B1; _b2 = coefficients.B2;
            _a1 = coefficients.A1; _a2 = coefficients.A2;
            _z1 = new double[channels]; _z2 = new double[channels];
        }
        public double Process(double x, int channel)
        {
            var y = _b0 * x + _z1[channel];
            _z1[channel] = _b1 * x - _a1 * y + _z2[channel];
            _z2[channel] = _b2 * x - _a2 * y;
            if (Math.Abs(_z1[channel]) < 1e-30) _z1[channel] = 0;
            if (Math.Abs(_z2[channel]) < 1e-30) _z2[channel] = 0;
            return y;
        }
    }
}
