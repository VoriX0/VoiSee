using System.Numerics;

namespace VoiSe.Audio;

// Streaming, single-channel online weighted prediction error (WPE).
// Adapted from nara_wpe.online_wpe_step, Paderborn University (MIT).
// Source and license are shipped in ThirdPartyLicenses. No model or GPU.
internal sealed class RoomEchoReducer
{
    private const int Size = 1024, Hop = 256, Bins = Size / 2 + 1, Taps = 8, Delay = 3, History = Taps + Delay + 1;
    private const double Alpha = .995;
    private readonly double[] _window = new double[Size], _input = new double[Size], _overlap = new double[Size], _power = new double[Bins];
    private readonly Complex[] _fft = new Complex[Size], _twiddles = new Complex[Size / 2];
    private readonly Complex[] _past = new Complex[History * Bins], _weights = new Complex[Bins * Taps], _cov = new Complex[Bins * Taps * Taps];
    private readonly Complex[] _x = new Complex[Taps], _v = new Complex[Taps];
    private readonly float[] _output = new float[Hop];
    private int _write, _pending, _out = Hop, _history;
    private double _energyPeak;
    public RoomEchoReducer()
    {
        for (var i = 0; i < Size; i++) _window[i] = Math.Sqrt(.5 - .5 * Math.Cos(2 * Math.PI * i / Size));
        for (var i = 0; i < Size / 2; i++) _twiddles[i] = Complex.FromPolarCoordinates(1, -2 * Math.PI * i / Size);
        for (var bin = 0; bin < Bins; bin++) ResetBin(bin);
    }
    public void Process(Span<float> stereo)
    {
        for (var i = 0; i + 1 < stereo.Length; i += 2)
        {
            var sample = (stereo[i] + stereo[i + 1]) * .5f;
            _input[_write] = float.IsFinite(sample) ? sample : 0; _write = (_write + 1) % Size;
            if (++_pending == Hop) { ProcessFrame(); _pending = 0; _out = 0; }
            var output = _out < Hop ? _output[_out++] : 0;
            stereo[i] = stereo[i + 1] = output;
        }
    }
    private void ProcessFrame()
    {
        double frameEnergy = 0;
        for (var i = 0; i < Size; i++)
        {
            var value = _input[(_write + i) % Size] * _window[i];
            _fft[i] = value; frameEnergy += value * value;
        }
        _energyPeak = Math.Max(frameEnergy, _energyPeak * .99);
        // Single-mic prediction can also predict voiced harmonics. Keep active
        // speech correction mild; use stronger cancellation in decaying tails.
        var amount = frameEnergy > _energyPeak * .35 ? .2 : .8;
        Transform(false);
        for (var bin = 0; bin < Bins; bin++)
        {
            var current = _fft[bin]; _past[_history * Bins + bin] = current;
            var baseW = bin * Taps; var baseP = bin * Taps * Taps;
            Complex prediction = 0;
            for (var k = 0; k < Taps; k++)
            {
                _x[k] = _past[((_history - Delay - k + History) % History) * Bins + bin];
                prediction += Complex.Conjugate(_weights[baseW + k]) * _x[k];
            }
            var residual = current - prediction;
            var energy = current.Magnitude * current.Magnitude;
            _power[bin] = .9 * _power[bin] + .1 * residual.Magnitude * residual.Magnitude;
            if (_power[bin] > 1e-8 && energy > 1e-10)
            {
                Complex denominator = Alpha * Math.Max(_power[bin], 1e-6);
                for (var row = 0; row < Taps; row++)
                {
                    Complex value = 0;
                    for (var col = 0; col < Taps; col++) value += _cov[baseP + row * Taps + col] * _x[col];
                    _v[row] = value; denominator += Complex.Conjugate(_x[row]) * value;
                }
                var divisor = Math.Max(1e-8, denominator.Real);
                for (var row = 0; row < Taps; row++)
                {
                    var gain = _v[row] / divisor;
                    _weights[baseW + row] += gain * Complex.Conjugate(residual);
                    for (var col = 0; col < Taps; col++)
                        _cov[baseP + row * Taps + col] = (_cov[baseP + row * Taps + col] - gain * Complex.Conjugate(_v[col])) / Alpha;
                }
            }
            // Conservative cancellation protects near-end speech and transients.
            var limit = current.Magnitude * .75;
            if (prediction.Magnitude > limit && prediction.Magnitude > 0) prediction *= limit / prediction.Magnitude;
            var cleaned = current - amount * prediction;
            if (!double.IsFinite(cleaned.Real) || !double.IsFinite(cleaned.Imaginary) || _cov[baseP].Magnitude > 1e8)
            { ResetBin(bin); cleaned = current; }
            _fft[bin] = cleaned;
            if (bin > 0 && bin < Size / 2) _fft[Size - bin] = Complex.Conjugate(cleaned);
        }
        _history = (_history + 1) % History;
        Transform(true);
        for (var i = 0; i < Size; i++) _overlap[i] += _fft[i].Real * _window[i] * .5;
        for (var i = 0; i < Hop; i++) _output[i] = (float)_overlap[i];
        Array.Copy(_overlap, Hop, _overlap, 0, Size - Hop); Array.Clear(_overlap, Size - Hop, Hop);
    }
    private void ResetBin(int bin)
    {
        Array.Clear(_weights, bin * Taps, Taps); Array.Clear(_cov, bin * Taps * Taps, Taps * Taps); _power[bin] = 0;
        for (var i = 0; i < Taps; i++) _cov[bin * Taps * Taps + i * Taps + i] = 1;
    }
    private void Transform(bool inverse)
    {
        for (int i = 1, j = 0; i < Size; i++)
        {
            var bit = Size >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
            if (i < j) (_fft[i], _fft[j]) = (_fft[j], _fft[i]);
        }
        for (var length = 2; length <= Size; length <<= 1)
        for (var start = 0; start < Size; start += length)
        for (var i = 0; i < length / 2; i++)
        {
            var twiddle = _twiddles[i * Size / length]; if (inverse) twiddle = Complex.Conjugate(twiddle);
            var even = _fft[start + i]; var odd = _fft[start + i + length / 2] * twiddle;
            _fft[start + i] = even + odd; _fft[start + i + length / 2] = even - odd;
        }
        if (inverse) for (var i = 0; i < Size; i++) _fft[i] /= Size;
    }
}
