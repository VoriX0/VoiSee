using NAudio.Wave;

namespace VoiSe.Audio;

public sealed class SoundboardTransport
{
    private readonly WaveFormat _format;
    private readonly object _sync = new();
    private ActiveSound? _active;

    public SoundboardTransport(WaveFormat format)
    {
        _format = format;
    }

    public bool IsPlaying
    {
        get
        {
            lock (_sync)
            {
                return _active is not null && !_active.IsPaused;
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _active is not null;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _active?.IsPaused ?? false;
            }
        }
    }

    public void Play(string filePath, float virtualVolume, float monitorVolume, int virtualDelayMs, bool loop = false)
    {
        var data = SoundFileLoader.LoadToFormat(filePath, _format);
        var delaySamples = Math.Max(0, (int)Math.Round(_format.SampleRate * (virtualDelayMs / 1000.0)) * _format.Channels);

        lock (_sync)
        {
            _active = new ActiveSound(
                data,
                _format.SampleRate,
                _format.Channels,
                Math.Clamp(virtualVolume, 0.0f, 2.0f),
                Math.Clamp(monitorVolume, 0.0f, 2.0f),
                delaySamples,
                loop);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _active = null;
        }
    }

    public bool TogglePause()
    {
        lock (_sync)
        {
            if (_active is null)
            {
                return false;
            }

            _active.TogglePause();
            return _active.IsPaused;
        }
    }

    public void Seek(double seconds)
    {
        lock (_sync)
        {
            _active?.Seek(seconds);
        }
    }

    public void UpdateVolumes(float virtualVolume, float monitorVolume)
    {
        lock (_sync)
        {
            _active?.UpdateVolumes(
                Math.Clamp(virtualVolume, 0.0f, 2.0f),
                Math.Clamp(monitorVolume, 0.0f, 2.0f));
        }
    }

    public SoundboardStatus GetStatus()
    {
        lock (_sync)
        {
            return _active?.GetStatus() ?? SoundboardStatus.Empty;
        }
    }

    public int Read(AudioRoute route, float[] buffer, int offset, int count)
    {
        lock (_sync)
        {
            if (_active is null)
            {
                Array.Clear(buffer, offset, count);
                return 0;
            }

            var written = _active.Read(route, buffer, offset, count);
            if (_active.IsFinished)
            {
                _active = null;
            }

            return written;
        }
    }

    private sealed class ActiveSound
    {
        private readonly float[] _samples;
        private readonly int _sampleRate;
        private readonly int _channels;
        private float _virtualVolume;
        private float _monitorVolume;
        private int _virtualPosition;
        private int _monitorPosition;
        private readonly int _initialVirtualDelaySamples;
        private int _remainingVirtualDelaySamples;
        private bool _virtualFinished;
        private bool _monitorFinished;
        private readonly bool _loop;

        public ActiveSound(float[] samples, int sampleRate, int channels, float virtualVolume, float monitorVolume, int virtualDelaySamples, bool loop)
        {
            _samples = samples;
            _sampleRate = sampleRate;
            _channels = channels;
            _virtualVolume = virtualVolume;
            _monitorVolume = monitorVolume;
            _initialVirtualDelaySamples = Math.Max(0, virtualDelaySamples);
            _remainingVirtualDelaySamples = _initialVirtualDelaySamples;
            _loop = loop;
        }

        public bool IsFinished => _samples.Length == 0 || (!_loop && _virtualFinished && _monitorFinished);
        public bool IsPaused { get; private set; }

        public void TogglePause()
        {
            IsPaused = !IsPaused;
        }

        public void UpdateVolumes(float virtualVolume, float monitorVolume)
        {
            _virtualVolume = virtualVolume;
            _monitorVolume = monitorVolume;
        }

        public void Seek(double seconds)
        {
            var sampleFrame = (int)Math.Round(Math.Max(0, seconds) * _sampleRate);
            var sampleIndex = Math.Clamp(sampleFrame * _channels, 0, _samples.Length);
            _monitorPosition = sampleIndex;
            _virtualPosition = sampleIndex;
            _remainingVirtualDelaySamples = _initialVirtualDelaySamples;
            _monitorFinished = _monitorPosition >= _samples.Length;
            _virtualFinished = _virtualPosition >= _samples.Length;
        }

        public SoundboardStatus GetStatus()
        {
            var durationSeconds = _samples.Length / (double)_channels / _sampleRate;
            var currentSeconds = Math.Min(durationSeconds, _monitorPosition / (double)_channels / _sampleRate);
            return new SoundboardStatus(true, IsPaused, currentSeconds, durationSeconds);
        }

        public int Read(AudioRoute route, float[] buffer, int offset, int count)
        {
            if (IsPaused)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            var written = 0;

            if (route == AudioRoute.VirtualMicrophone && _remainingVirtualDelaySamples > 0)
            {
                var silence = Math.Min(count, _remainingVirtualDelaySamples);
                Array.Clear(buffer, offset, silence);
                _remainingVirtualDelaySamples -= silence;
                offset += silence;
                count -= silence;
                written += silence;
            }

            if (count <= 0)
            {
                return written;
            }

            if (_samples.Length == 0)
            {
                Array.Clear(buffer, offset, count);
                _virtualFinished = true;
                _monitorFinished = true;
                return written;
            }

            var position = route == AudioRoute.VirtualMicrophone ? _virtualPosition : _monitorPosition;
            var volume = route == AudioRoute.VirtualMicrophone ? _virtualVolume : _monitorVolume;

            if (_loop)
            {
                var copied = 0;
                while (copied < count)
                {
                    if (position >= _samples.Length)
                    {
                        position = 0;
                    }

                    var remaining = Math.Max(0, _samples.Length - position);
                    var toCopy = Math.Min(count - copied, remaining);
                    if (toCopy <= 0)
                    {
                        break;
                    }

                    for (var i = 0; i < toCopy; i++)
                    {
                        buffer[offset + copied + i] = _samples[position + i] * volume;
                    }

                    position += toCopy;
                    copied += toCopy;
                }

                if (position >= _samples.Length)
                {
                    position = 0;
                }

                if (route == AudioRoute.VirtualMicrophone)
                {
                    _virtualPosition = position;
                    _virtualFinished = false;
                }
                else
                {
                    _monitorPosition = position;
                    _monitorFinished = false;
                }

                return written + copied;
            }

            var remainingOnce = Math.Max(0, _samples.Length - position);
            var toCopyOnce = Math.Min(count, remainingOnce);

            for (var i = 0; i < toCopyOnce; i++)
            {
                buffer[offset + i] = _samples[position + i] * volume;
            }

            if (toCopyOnce < count)
            {
                Array.Clear(buffer, offset + toCopyOnce, count - toCopyOnce);
            }

            if (route == AudioRoute.VirtualMicrophone)
            {
                _virtualPosition += toCopyOnce;
                _virtualFinished = _virtualPosition >= _samples.Length && _remainingVirtualDelaySamples == 0;
            }
            else
            {
                _monitorPosition += toCopyOnce;
                _monitorFinished = _monitorPosition >= _samples.Length;
            }

            return written + toCopyOnce;
        }
    }
}

public readonly record struct SoundboardStatus(bool IsActive, bool IsPaused, double CurrentSeconds, double DurationSeconds)
{
    public static SoundboardStatus Empty { get; } = new(false, false, 0, 0);
}
