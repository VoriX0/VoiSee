using System.Numerics;

namespace VoiSe.Audio;

public enum EqualizerFilter { Peak, LowShelf, HighShelf, HighPass, LowPass }

public sealed record EqualizerBand
{
    public bool Enabled { get; set; } = true;
    public double Frequency { get; set; } = 1000;
    public double GainDb { get; set; }
    public double Q { get; set; } = 1;
    public EqualizerFilter Filter { get; set; }
    public double WidthRatio => Math.Exp(Math.Asinh(1 / (2 * Q)));
    public void SetWidthFromEdge(double edgeFrequency)
    {
        var distance = Math.Max(.001, Math.Abs(Math.Log(edgeFrequency / Frequency)));
        Q = Math.Clamp(1 / (2 * Math.Sinh(distance)), .2, 12);
    }
    public EqualizerBand NormalizedCopy() => new()
    {
        Enabled = Enabled, Frequency = Safe(Frequency, 20, 20000, 1000),
        GainDb = Safe(GainDb, -12, 12, 0), Q = Safe(Q, .2, 12, 1),
        Filter = Enum.IsDefined(Filter) ? Filter : EqualizerFilter.Peak
    };
    private static double Safe(double x, double min, double max, double fallback) => double.IsFinite(x) ? Math.Clamp(x, min, max) : fallback;
}

// Shared coefficients for DSP and the displayed response, at the engine's 48kHz.
// RBJ: https://www.w3.org/TR/audio-eq-cookbook/
public readonly record struct EqualizerCoefficients(double B0, double B1, double B2, double A1, double A2)
{
    public static EqualizerCoefficients Create(double frequency, double gain, double q, int rate, EqualizerFilter filter = EqualizerFilter.Peak)
    {
        var w = 2 * Math.PI * frequency / rate; var c = Math.Cos(w); var sin = Math.Sin(w);
        var a = Math.Pow(10, gain / 40); var alpha = sin / (2 * q);
        double b0, b1, b2, a0, a1, a2;
        switch (filter)
        {
            case EqualizerFilter.HighPass:
                b0 = (1 + c) / 2; b1 = -(1 + c); b2 = b0; a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha; break;
            case EqualizerFilter.LowPass:
                b0 = (1 - c) / 2; b1 = 1 - c; b2 = b0; a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha; break;
            case EqualizerFilter.LowShelf:
                var low = 2 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) - (a - 1) * c + low); b1 = 2 * a * ((a - 1) - (a + 1) * c); b2 = a * ((a + 1) - (a - 1) * c - low);
                a0 = (a + 1) + (a - 1) * c + low; a1 = -2 * ((a - 1) + (a + 1) * c); a2 = (a + 1) + (a - 1) * c - low; break;
            case EqualizerFilter.HighShelf:
                var high = 2 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) + (a - 1) * c + high); b1 = -2 * a * ((a - 1) + (a + 1) * c); b2 = a * ((a + 1) + (a - 1) * c - high);
                a0 = (a + 1) - (a - 1) * c + high; a1 = 2 * ((a - 1) - (a + 1) * c); a2 = (a + 1) - (a - 1) * c - high; break;
            default:
                b0 = 1 + alpha * a; b1 = -2 * c; b2 = 1 - alpha * a; a0 = 1 + alpha / a; a1 = -2 * c; a2 = 1 - alpha / a; break;
        }
        return new(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }
    public double ResponseDb(double frequency, int rate = 48000)
    {
        var z = Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / rate);
        var magnitude = ((B0 + B1 * z + B2 * z * z) / (1 + A1 * z + A2 * z * z)).Magnitude;
        return 20 * Math.Log10(Math.Max(1e-12, magnitude));
    }
}
