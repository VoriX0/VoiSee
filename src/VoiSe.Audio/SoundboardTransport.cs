using NAudio.Wave;

namespace VoiSe.Audio;

public sealed class SoundboardTransport
{
    private readonly WaveFormat _format;
    private readonly object _sync = new();
    private ActiveSound? _primary;
    private readonly List<ActiveSound> _overlays = new();

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
                return (_primary is not null && !_primary.IsPaused) || _overlays.Any(sound => !sound.IsPaused);
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _primary is not null || _overlays.Count > 0;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                if (_primary is not null)
                {
                    return _primary.IsPaused;
                }

                return _overlays.Count > 0 && _overlays.All(sound => sound.IsPaused);
            }
        }
    }

    public void Play(string filePath, float virtualVolume, float monitorVolume, int virtualDelayMs, bool loop = false, string? playbackKey = null)
    {
        var data = SoundFileLoader.LoadToFormat(filePath, _format);
        var delaySamples = Math.Max(0, (int)Math.Round(_format.SampleRate * (virtualDelayMs / 1000.0)) * _format.Channels);
        var sound = new ActiveSound(
            data,
            _format.SampleRate,
            _format.Channels,
            Math.Clamp(virtualVolume, 0.0f, 2.0f),
            Math.Clamp(monitorVolume, 0.0f, 2.0f),
            delaySamples,
            loop,
            playbackKey);

        lock (_sync)
        {
            if (loop)
            {
                _primary = sound;
                _overlays.Clear();
                return;
            }

            if (!string.IsNullOrWhiteSpace(playbackKey))
            {
                RemoveByKey(playbackKey);
                _overlays.Add(sound);
                RemoveFinishedOverlays();
                return;
            }

            if (_primary is not null && _primary.IsLoop && !_primary.IsFinished)
            {
                _overlays.Add(sound);
                RemoveFinishedOverlays();
                return;
            }

            _primary = sound;
            _overlays.Clear();
        }
    }

    public void Stop(string? playbackKey = null)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(playbackKey))
            {
                _primary = null;
                _overlays.Clear();
                return;
            }

            RemoveByKey(playbackKey);
        }
    }

    public bool TogglePause(string? playbackKey = null)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(playbackKey))
            {
                if (_primary is null && _overlays.Count == 0)
                {
                    return false;
                }

                var newPausedState = !(_primary?.IsPaused ?? _overlays.All(sound => sound.IsPaused));
                _primary?.SetPaused(newPausedState);
                foreach (var overlay in _overlays)
                {
                    overlay.SetPaused(newPausedState);
                }

                return newPausedState;
            }

            var targets = FindByKey(playbackKey).ToList();
            if (targets.Count == 0)
            {
                return false;
            }

            var newState = !targets.All(sound => sound.IsPaused);
            foreach (var target in targets)
            {
                target.SetPaused(newState);
            }

            return newState;
        }
    }

    public void Seek(double seconds, string? playbackKey = null)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(playbackKey))
            {
                _primary?.Seek(seconds);
                return;
            }

            foreach (var target in FindByKey(playbackKey))
            {
                target.Seek(seconds);
            }
        }
    }

    public void UpdateVolumes(float virtualVolume, float monitorVolume, string? playbackKey = null)
    {
        var clampedVirtual = Math.Clamp(virtualVolume, 0.0f, 2.0f);
        var clampedMonitor = Math.Clamp(monitorVolume, 0.0f, 2.0f);

        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(playbackKey))
            {
                _primary?.UpdateVolumes(clampedVirtual, clampedMonitor);
                foreach (var overlay in _overlays)
                {
                    overlay.UpdateVolumes(clampedVirtual, clampedMonitor);
                }

                return;
            }

            foreach (var target in FindByKey(playbackKey))
            {
                target.UpdateVolumes(clampedVirtual, clampedMonitor);
            }
        }
    }

    public SoundboardStatus GetStatus(string? playbackKey = null)
    {
        lock (_sync)
        {
            RemoveFinishedOverlays();
            if (_primary is not null && _primary.IsFinished)
            {
                _primary = null;
            }

            if (!string.IsNullOrWhiteSpace(playbackKey))
            {
                return FindByKey(playbackKey).FirstOrDefault()?.GetStatus() ?? SoundboardStatus.Empty;
            }

            return _primary?.GetStatus()
                ?? _overlays.FirstOrDefault()?.GetStatus()
                ?? SoundboardStatus.Empty;
        }
    }

    public int Read(AudioRoute route, float[] buffer, int offset, int count)
    {
        lock (_sync)
        {
            Array.Clear(buffer, offset, count);

            if (_primary is null && _overlays.Count == 0)
            {
                return 0;
            }

            MixSound(route, _primary, buffer, offset, count);
            if (_primary is not null && _primary.IsFinished)
            {
                _primary = null;
            }

            for (var index = _overlays.Count - 1; index >= 0; index--)
            {
                var overlay = _overlays[index];
                MixSound(route, overlay, buffer, offset, count);
                if (overlay.IsFinished)
                {
                    _overlays.RemoveAt(index);
                }
            }

            return count;
        }
    }

    private static void MixSound(AudioRoute route, ActiveSound? sound, float[] output, int offset, int count)
    {
        if (sound is null)
        {
            return;
        }

        var scratch = new float[count];
        sound.Read(route, scratch, 0, count);
        for (var i = 0; i < count; i++)
        {
            output[offset + i] += scratch[i];
        }
    }

    private IEnumerable<ActiveSound> FindByKey(string playbackKey)
    {
        if (_primary is not null && string.Equals(_primary.PlaybackKey, playbackKey, StringComparison.OrdinalIgnoreCase))
        {
            yield return _primary;
        }

        foreach (var overlay in _overlays)
        {
            if (string.Equals(overlay.PlaybackKey, playbackKey, StringComparison.OrdinalIgnoreCase))
            {
                yield return overlay;
            }
        }
    }

    private void RemoveByKey(string playbackKey)
    {
        if (_primary is not null && string.Equals(_primary.PlaybackKey, playbackKey, StringComparison.OrdinalIgnoreCase))
        {
            _primary = null;
        }

        _overlays.RemoveAll(sound => string.Equals(sound.PlaybackKey, playbackKey, StringComparison.OrdinalIgnoreCase));
    }

    private void RemoveFinishedOverlays()
    {
        for (var index = _overlays.Count - 1; index >= 0; index--)
        {
            if (_overlays[index].IsFinished)
            {
                _overlays.RemoveAt(index);
            }
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

        public ActiveSound(float[] samples, int sampleRate, int channels, float virtualVolume, float monitorVolume, int virtualDelaySamples, bool loop, string? playbackKey)
        {
            _samples = samples;
            _sampleRate = sampleRate;
            _channels = channels;
            _virtualVolume = virtualVolume;
            _monitorVolume = monitorVolume;
            _initialVirtualDelaySamples = Math.Max(0, virtualDelaySamples);
            _remainingVirtualDelaySamples = _initialVirtualDelaySamples;
            IsLoop = loop;
            PlaybackKey = playbackKey;
        }

        public bool IsLoop { get; }
        public string? PlaybackKey { get; }
        public bool IsFinished => _samples.Length == 0 || (!IsLoop && _virtualFinished && _monitorFinished);
        public bool IsPaused { get; private set; }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
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

            if (IsLoop)
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
