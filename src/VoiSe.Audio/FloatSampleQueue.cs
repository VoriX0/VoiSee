namespace VoiSe.Audio;

// Bounded, fixed-allocation ring; overflow drops oldest audio, never expands RAM.
internal sealed class FloatSampleQueue
{
    private readonly float[] _buffer;
    private readonly object _sync = new();
    private int _head, _count;
    private long _dropped;
    public FloatSampleQueue(int maxSamples) => _buffer = new float[Math.Max(1, maxSamples)];
    public int Count { get { lock (_sync) return _count; } }
    public long DroppedSampleCount { get { lock (_sync) return _dropped; } }
    public void Add(ReadOnlySpan<float> samples)
    {
        lock (_sync)
        {
            _dropped += Math.Max(0, _count + samples.Length - _buffer.Length);
            if (samples.Length >= _buffer.Length)
            {
                samples[^_buffer.Length..].CopyTo(_buffer);
                _head = 0; _count = _buffer.Length; return;
            }
            var discard = Math.Min(Math.Max(0, _count + samples.Length - _buffer.Length), _count);
            _head = (_head + discard) % _buffer.Length; _count -= discard;
            var tail = (_head + _count) % _buffer.Length;
            var first = Math.Min(samples.Length, _buffer.Length - tail);
            samples[..first].CopyTo(_buffer.AsSpan(tail));
            samples[first..].CopyTo(_buffer);
            _count += samples.Length;
        }
    }
    public int Read(float[] buffer, int offset, int count)
    {
        var output = buffer.AsSpan(offset, count);
        lock (_sync)
        {
            var available = Math.Min(count, _count);
            var first = Math.Min(available, _buffer.Length - _head);
            _buffer.AsSpan(_head, first).CopyTo(output);
            _buffer.AsSpan(0, available - first).CopyTo(output[first..]);
            output[available..].Clear();
            _head = (_head + available) % _buffer.Length; _count -= available;
            return available;
        }
    }
    public void Clear() { lock (_sync) { _head = 0; _count = 0; } }
}
