using NAudio.Wave;

namespace VoiSe.Audio;

internal sealed class MediaBridgeTransport
{
    private readonly FloatSampleQueue _queue;
    private readonly object _sync = new();
    private float _volume = 1.0f;
    private bool _paused;
    private float _peak;
    private DateTime _lastPeakUpdateUtc = DateTime.MinValue;

    public MediaBridgeTransport(WaveFormat waveFormat)
    {
        var maxSamples = waveFormat.SampleRate * waveFormat.Channels * 2;
        _queue = new FloatSampleQueue(maxSamples);
    }

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _paused;
            }
        }
    }

    public void AddSamples(ReadOnlySpan<float> samples)
    {
        bool paused;
        lock (_sync)
        {
            paused = _paused;
        }

        var peak = 0.0f;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        lock (_sync)
        {
            _peak = Math.Clamp(peak, 0.0f, 1.0f);
            _lastPeakUpdateUtc = DateTime.UtcNow;
        }

        if (!paused)
        {
            _queue.Add(samples);
        }
    }

    public void Read(float[] buffer, int offset, int count)
    {
        float volume;
        bool paused;
        lock (_sync)
        {
            volume = _volume;
            paused = _paused;
        }

        if (paused)
        {
            Array.Clear(buffer, offset, count);
            _queue.Clear();
            return;
        }

        _queue.Read(buffer, offset, count);
        for (var i = 0; i < count; i++)
        {
            buffer[offset + i] *= volume;
        }
    }

    public void SetVolume(float volume)
    {
        lock (_sync)
        {
            _volume = Math.Clamp(volume, 0.0f, 1.5f);
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_sync)
        {
            _paused = paused;
        }

        _queue.Clear();
    }

    public float GetDisplayPeak()
    {
        lock (_sync)
        {
            if ((DateTime.UtcNow - _lastPeakUpdateUtc).TotalMilliseconds > 500)
            {
                _peak = 0.0f;
            }

            return _peak;
        }
    }

    public void Clear()
    {
        _queue.Clear();
        lock (_sync)
        {
            _paused = false;
            _peak = 0.0f;
            _lastPeakUpdateUtc = DateTime.MinValue;
        }
    }
}
