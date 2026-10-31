using NAudio.Wave;

namespace VoiSe.Audio;

// Stateful microphone conversion. Reuses storage and carries fractional resampler
// position across WASAPI packets instead of rounding every packet independently.
internal sealed class CaptureSampleConverter
{
    private readonly WaveFormat _source, _target;
    private float[] _buffer = Array.Empty<float>();
    private long _framesSeen;
    private double _nextPosition;
    private float _previous;
    public CaptureSampleConverter(WaveFormat source, WaveFormat target)
    {
        if (!PcmFloatConverter.CanProcess(source)) throw new NotSupportedException($"Unsupported capture format: {source}");
        if (source.Channels <= 0 || target.Channels <= 0 || source.SampleRate <= 0 || target.SampleRate <= 0)
            throw new ArgumentException("Invalid capture format.");
        _source = source; _target = target;
    }
    public Span<float> Convert(byte[] bytes, int count)
    {
        if (count < 0 || count > bytes.Length || count % _source.BlockAlign != 0)
            throw new ArgumentException("Capture packet is not frame aligned.");
        var frames = count / _source.BlockAlign;
        if (frames == 0) return Span<float>.Empty;
        var needed = checked(((int)Math.Ceiling(frames * (double)_target.SampleRate / _source.SampleRate) + 2) * _target.Channels);
        if (_buffer.Length < needed) _buffer = new float[needed];
        var used = 0;
        if (_source.SampleRate == _target.SampleRate)
        {
            for (var f = 0; f < frames; f++)
            {
                var mono = ReadMono(bytes, f);
                for (var c = 0; c < _target.Channels; c++) _buffer[used++] = mono;
            }
        }
        else
        {
            var last = _framesSeen + frames - 1;
            var step = _source.SampleRate / (double)_target.SampleRate;
            while (_nextPosition <= last)
            {
                var left = (long)Math.Floor(_nextPosition);
                var fraction = (float)(_nextPosition - left);
                if (left == last && fraction > 0) break; // Next packet supplies the right sample.
                var a = left < _framesSeen ? _previous : ReadMono(bytes, checked((int)(left - _framesSeen)));
                var b = left == last ? a : ReadMono(bytes, checked((int)(left + 1 - _framesSeen)));
                var mono = a + (b - a) * fraction;
                for (var c = 0; c < _target.Channels; c++) _buffer[used++] = mono;
                _nextPosition += step;
            }
        }
        _previous = ReadMono(bytes, frames - 1); _framesSeen += frames;
        return _buffer.AsSpan(0, used);
    }
    private float ReadMono(byte[] bytes, int frame)
    {
        var sum = 0f;
        var offset = frame * _source.BlockAlign;
        var sampleBytes = _source.BitsPerSample / 8;
        for (var c = 0; c < _source.Channels; c++)
        {
            var sample = sampleBytes == 4 ? BitConverter.ToSingle(bytes, offset + c * sampleBytes)
                : BitConverter.ToInt16(bytes, offset + c * sampleBytes) / 32768f;
            sum += float.IsFinite(sample) ? sample : 0f;
        }
        return sum / _source.Channels;
    }
}
