using NAudio.Wave;

namespace VoiSe.Audio;

internal sealed class MediaBridgeTransport
{
    private readonly FloatSampleQueue _queue;
    private readonly object _sync = new();
    private float _volume = 1.0f;
    private bool _paused;
    private float _sourcePeak;
    private float _outputPeak;
    private DateTime _lastSourcePeakUpdateUtc = DateTime.MinValue;
    private DateTime _lastOutputPeakUpdateUtc = DateTime.MinValue;

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
            _sourcePeak = Math.Clamp(peak, 0.0f, 1.0f);
            _lastSourcePeakUpdateUtc = DateTime.UtcNow;
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
            lock (_sync)
            {
                _outputPeak = 0.0f;
                _lastOutputPeakUpdateUtc = DateTime.UtcNow;
            }
            return;
        }

        _queue.Read(buffer, offset, count);
        var outputPeak = 0.0f;
        for (var i = 0; i < count; i++)
        {
            buffer[offset + i] *= volume;
            outputPeak = Math.Max(outputPeak, Math.Abs(buffer[offset + i]));
        }

        lock (_sync)
        {
            _outputPeak = Math.Clamp(outputPeak, 0.0f, 1.0f);
            _lastOutputPeakUpdateUtc = DateTime.UtcNow;
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

    public float GetSourceDisplayPeak()
    {
        lock (_sync)
        {
            if ((DateTime.UtcNow - _lastSourcePeakUpdateUtc).TotalMilliseconds > 500)
            {
                _sourcePeak = 0.0f;
            }

            return _sourcePeak;
        }
    }

    public float GetOutputDisplayPeak()
    {
        lock (_sync)
        {
            if ((DateTime.UtcNow - _lastOutputPeakUpdateUtc).TotalMilliseconds > 500)
            {
                _outputPeak = 0.0f;
            }

            return _outputPeak;
        }
    }

    public void Clear()
    {
        _queue.Clear();
        lock (_sync)
        {
            _paused = false;
            _sourcePeak = 0.0f;
            _outputPeak = 0.0f;
            _lastSourcePeakUpdateUtc = DateTime.MinValue;
            _lastOutputPeakUpdateUtc = DateTime.MinValue;
        }
    }
}
