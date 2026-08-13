namespace VoiSe.Audio;

internal sealed class FloatSampleQueue
{
    private readonly Queue<float> _samples = new();
    private readonly int _maxSamples;
    private readonly object _sync = new();
    private long _droppedSampleCount;

    public FloatSampleQueue(int maxSamples)
    {
        _maxSamples = Math.Max(1, maxSamples);
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _samples.Count;
            }
        }
    }


    public long DroppedSampleCount
    {
        get
        {
            lock (_sync)
            {
                return _droppedSampleCount;
            }
        }
    }

    public void Add(ReadOnlySpan<float> samples)
    {
        lock (_sync)
        {
            foreach (var sample in samples)
            {
                _samples.Enqueue(sample);
            }

            while (_samples.Count > _maxSamples)
            {
                _samples.Dequeue();
                _droppedSampleCount++;
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_sync)
        {
            var available = Math.Min(count, _samples.Count);
            for (var i = 0; i < available; i++)
            {
                buffer[offset + i] = _samples.Dequeue();
            }

            if (available < count)
            {
                Array.Clear(buffer, offset + available, count - available);
            }

            return available;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _samples.Clear();
        }
    }
}
