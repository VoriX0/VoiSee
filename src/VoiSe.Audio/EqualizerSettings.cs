namespace VoiSe.Audio;

// Graphic correction plus independent parametric filters. Displaying ten
// sliders never discards the stored 31-band detail.
public sealed class EqualizerSettings
{
    public static IReadOnlyList<double> Frequencies { get; } = Array.AsReadOnly(new double[]
    { 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000, 20000 });
    public static IReadOnlyList<int> BasicBands { get; } = Array.AsReadOnly(new[] { 2, 5, 8, 11, 14, 17, 20, 23, 26, 29 });
    public bool Enabled { get; set; } = true;
    public double PreampDb { get; set; }
    public double[] GainsDb { get; set; } = new double[31];
    public double[] ToneGainsDb { get; set; } = new double[3];
    public EqualizerBand[] ParametricBands { get; set; } = CreateParametricBands();
    private static EqualizerBand[] CreateParametricBands() => new[] { 35.0, 120, 1000, 6000, 18000 }
        .Select(f => new EqualizerBand { Frequency = f }).ToArray();
    public EqualizerSettings NormalizedCopy()
    {
        var copy = new EqualizerSettings { Enabled = Enabled, PreampDb = Limit(PreampDb, -24, 12) };
        for (var i = 0; i < Math.Min(GainsDb?.Length ?? 0, copy.GainsDb.Length); i++)
            copy.GainsDb[i] = Limit(GainsDb![i], -12, 12);
        for (var i = 0; i < Math.Min(ToneGainsDb?.Length ?? 0, 3); i++)
            copy.ToneGainsDb[i] = Limit(ToneGainsDb![i], -6, 6);
        for (var i = 0; i < Math.Min(ParametricBands?.Length ?? 0, 5); i++)
            copy.ParametricBands[i] = ParametricBands![i]?.NormalizedCopy() ?? copy.ParametricBands[i];
        return copy;
    }
    public EqualizerCoefficients[] GetResponseFilters()
    {
        var filters = new List<EqualizerCoefficients>();
        for (var i = 0; i < 31; i++)
            if (Math.Abs(GainsDb[i]) > .00001) filters.Add(EqualizerCoefficients.Create(Frequencies[i], GainsDb[i], 4.318473046963146, 48000));
        foreach (var band in ParametricBands)
            if (band.Enabled && (Math.Abs(band.GainDb) > .00001 || band.Filter is EqualizerFilter.HighPass or EqualizerFilter.LowPass))
                filters.Add(EqualizerCoefficients.Create(band.Frequency, band.GainDb, band.Q, 48000, band.Filter));
        return filters.ToArray();
    }
    // Broad, additive controls keep existing detailed adjustments intact.
    public void SetToneGain(int tone, double value)
    {
        if (tone < 0 || tone > 2) throw new ArgumentOutOfRangeException(nameof(tone));
        var centers = new[] { 125.0, 2000.0, 8000.0 };
        value = Limit(value, -6, 6);
        var delta = value - ToneGainsDb[tone];
        for (var i = 0; i < 31; i++)
        {
            var distance = Math.Log2(Frequencies[i] / centers[tone]);
            var weight = Math.Max(0, 1 - Math.Abs(distance) / 2);
            GainsDb[i] = Limit(GainsDb[i] + delta * weight, -12, 12);
        }
        ToneGainsDb[tone] = value;
    }

    public static EqualizerSettings FromPreset(int preset)
    {
        var settings = new EqualizerSettings();
        switch (preset)
        {
            case 0: break;
            case 1: settings.SetToneGain(0, 2); settings.SetToneGain(2, -1); settings.PreampDb = -3; break;
            case 2: settings.SetToneGain(0, -2); settings.SetToneGain(1, 2); settings.PreampDb = -3; break;
            case 3: settings.SetToneGain(1, -1); settings.SetToneGain(2, -3); break;
            default: throw new ArgumentOutOfRangeException(nameof(preset));
        }
        return settings;
    }
    // Coarse edits apply a log-frequency tent to adjacent bands, preserving the
    // existing fine-detail offsets. Switching views alone does no conversion.
    public void SetBasicGain(int basicBand, double value)
    {
        if (basicBand < 0 || basicBand >= BasicBands.Count) throw new ArgumentOutOfRangeException(nameof(basicBand));
        var center = BasicBands[basicBand];
        var left = basicBand == 0 ? -1 : BasicBands[basicBand - 1];
        var right = basicBand == BasicBands.Count - 1 ? 31 : BasicBands[basicBand + 1];
        var delta = Limit(value, -12, 12) - GainsDb[center];
        for (var i = left + 1; i < right; i++)
        {
            var weight = i == center || left < 0 && i < center || right == 31 && i > center ? 1.0
                : i < center ? Math.Log(Frequencies[i] / Frequencies[left]) / Math.Log(Frequencies[center] / Frequencies[left])
                : Math.Log(Frequencies[right] / Frequencies[i]) / Math.Log(Frequencies[right] / Frequencies[center]);
            GainsDb[i] = Limit(GainsDb[i] + delta * weight, -12, 12);
        }
    }
    private static double Limit(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : 0;
}
