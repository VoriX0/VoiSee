using NAudio.Wave;

namespace VoiSe.Audio;

public sealed class SoundboardTransport : IDisposable
{
    private readonly WaveFormat _format;
    private readonly object _sync = new();
    private ActiveSound? _primary;
    private readonly bool _monitorEnabled;
    private float[] _scratch = Array.Empty<float>();
    private long _generation, _nextRequest;
    private readonly Dictionary<string, LoadRequest> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadSlots = new(2, 2);
    private sealed record LoadRequest(long Id, long Generation, string Key, CancellationTokenSource Cancellation);
    private bool _disposed;
    public string? LastError { get; private set; }
    private readonly List<ActiveSound> _overlays = new();

    public SoundboardTransport(WaveFormat format, bool monitorEnabled = true)
    {
        _format = format;
        _monitorEnabled = monitorEnabled;
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

    private LoadRequest RegisterLoad(string? playbackKey, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var key = playbackKey ?? "__primary";
            if (_pending.TryGetValue(key, out var previous)) previous.Cancellation.Cancel();
            else if (_pending.Count >= 64) throw new InvalidOperationException("Too many pending sounds (64).");
            var request = new LoadRequest(++_nextRequest, _generation, key, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            _pending[key] = request;
            return request;
        }
    }

    public void Play(string filePath, float virtualVolume, float monitorVolume, int virtualDelayMs, bool loop = false, string? playbackKey = null)
    {
        var request = RegisterLoad(playbackKey, CancellationToken.None);
        var admitted = false;
        try
        {
            _loadSlots.Wait(request.Cancellation.Token); admitted = true;
            LoadAndCommit(request, filePath, virtualVolume, monitorVolume, virtualDelayMs, loop, playbackKey);
        }
        finally { if (admitted) _loadSlots.Release(); FinishLoad(request); }
    }

    public Task PlayAsync(string filePath, float virtualVolume, float monitorVolume, int virtualDelayMs,
        bool loop = false, string? playbackKey = null, CancellationToken cancellationToken = default)
    {
        // Register before scheduling: Stop must invalidate a command even before its worker starts.
        var request = RegisterLoad(playbackKey, cancellationToken);
        return RunLoadAsync(request, filePath, virtualVolume, monitorVolume, virtualDelayMs, loop, playbackKey);
    }

    private async Task RunLoadAsync(LoadRequest request, string filePath, float virtualVolume, float monitorVolume, int virtualDelayMs, bool loop, string? playbackKey)
    {
        var admitted = false;
        try
        {
            await _loadSlots.WaitAsync(request.Cancellation.Token).ConfigureAwait(false); admitted = true;
            await Task.Run(() => LoadAndCommit(request, filePath, virtualVolume, monitorVolume, virtualDelayMs, loop, playbackKey), request.Cancellation.Token).ConfigureAwait(false);
        }
        finally { if (admitted) _loadSlots.Release(); FinishLoad(request); }
    }

    private void FinishLoad(LoadRequest request)
    {
        lock (_sync)
            if (_pending.TryGetValue(request.Key, out var current) && ReferenceEquals(current, request)) _pending.Remove(request.Key);
        request.Cancellation.Dispose();
    }

    private void LoadAndCommit(LoadRequest request, string filePath, float virtualVolume, float monitorVolume, int virtualDelayMs, bool loop, string? playbackKey)
    {
        request.Cancellation.Token.ThrowIfCancellationRequested();
        var sampleCount = SoundFileLoader.GetSampleCount(filePath, _format);
        var data = sampleCount * sizeof(float) <= SoundFileLoader.ShortClipBytes
            ? SoundFileLoader.LoadToFormat(filePath, _format, request.Cancellation.Token) : null;
        var delaySamples = Math.Max(0, (int)Math.Round(_format.SampleRate * (virtualDelayMs / 1000.0)) * _format.Channels);
        var sound = new ActiveSound(
            data, data?.LongLength ?? sampleCount, filePath, _format, _monitorEnabled,
            _format.SampleRate,
            _format.Channels,
            Math.Clamp(virtualVolume, 0.0f, 2.0f),
            Math.Clamp(monitorVolume, 0.0f, 2.0f),
            delaySamples,
            loop,
            playbackKey);

        lock (_sync)
        {
            if (_disposed || request.Cancellation.IsCancellationRequested || request.Generation != _generation
                || !_pending.TryGetValue(request.Key, out var current) || !ReferenceEquals(current, request))
            { sound.Dispose(); throw new OperationCanceledException(request.Cancellation.Token); }
            _pending.Remove(request.Key);
            RemoveFinishedOverlays();
            if (!loop && _overlays.Count >= 32 && !FindByKey(playbackKey ?? "").Any()) { sound.Dispose(); throw new InvalidOperationException("Too many simultaneous sounds (32)."); }
            if (loop)
            {
                _primary?.Dispose();
                _primary = sound;
                foreach (var active in _overlays) active.Dispose();
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

            _primary?.Dispose();
            _primary = sound;
            foreach (var active in _overlays) active.Dispose();
            _overlays.Clear();
        }
    }

    public void Stop(string? playbackKey = null)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(playbackKey))
            {
                _generation++;
                var pendingRequests = _pending.Values.ToArray();
                _pending.Clear();
                foreach (var pendingRequest in pendingRequests) pendingRequest.Cancellation.Cancel();
                _primary?.Dispose();
                _primary = null;
                foreach (var active in _overlays) active.Dispose();
                _overlays.Clear();
                return;
            }

            if (_pending.Remove(playbackKey, out var request)) request.Cancellation.Cancel();
            RemoveByKey(playbackKey);
        }
    }

    public void Dispose() { lock (_sync) { if (_disposed) return; _disposed = true; Stop(); } }

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


    public void UpdateLoop(bool loop, string? playbackKey = null)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(playbackKey))
            {
                _primary?.SetLoop(loop);
                return;
            }

            foreach (var target in FindByKey(playbackKey))
            {
                target.SetLoop(loop);
            }
        }
    }

    public SoundboardStatus GetStatus(string? playbackKey = null)
    {
        lock (_sync)
        {
            if (_primary?.Error is { } error) LastError = error;
            RemoveFinishedOverlays();
            if (_primary is not null && _primary.IsFinished)
            {
                _primary?.Dispose();
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
                _primary?.Dispose();
                _primary = null;
            }

            for (var index = _overlays.Count - 1; index >= 0; index--)
            {
                var overlay = _overlays[index];
                MixSound(route, overlay, buffer, offset, count);
                if (overlay.IsFinished)
                {
                    _overlays[index].Dispose();
                    _overlays.RemoveAt(index);
                }
            }

            return count;
        }
    }

    private void MixSound(AudioRoute route, ActiveSound? sound, float[] output, int offset, int count)
    {
        if (sound is null)
        {
            return;
        }

        if (_scratch.Length < count) _scratch = new float[count];
        var scratch = _scratch;
        sound.Read(route, scratch, 0, count);
        if (sound.Error is { } error) LastError = error;
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
            _primary?.Dispose();
            _primary = null;
        }

        for (var i = _overlays.Count - 1; i >= 0; i--)
            if (string.Equals(_overlays[i].PlaybackKey, playbackKey, StringComparison.OrdinalIgnoreCase))
            { _overlays[i].Dispose(); _overlays.RemoveAt(i); }
    }

    private void RemoveFinishedOverlays()
    {
        for (var index = _overlays.Count - 1; index >= 0; index--)
        {
            if (_overlays[index].IsFinished)
            {
                if (_overlays[index].Error is { } error) LastError = error;
                _overlays[index].Dispose();
                _overlays.RemoveAt(index);
            }
        }
    }

    private sealed class ActiveSound : IDisposable
    {
        private readonly float[]? _samples;
        private readonly long _sampleCount;
        private readonly bool _hasMonitor;
        private readonly ReadAheadSound? _stream;
        public string? Error => _stream?.Error;
        public void Dispose() => _stream?.Dispose();
        private readonly int _sampleRate;
        private readonly int _channels;
        private float _virtualVolume;
        private float _monitorVolume;
        private long _virtualPosition;
        private long _monitorPosition;
        private readonly int _initialVirtualDelaySamples;
        private int _remainingVirtualDelaySamples;
        private bool _virtualFinished;
        private bool _monitorFinished;

        public ActiveSound(float[]? samples, long sampleCount, string path, WaveFormat format, bool hasMonitor, int sampleRate, int channels, float virtualVolume, float monitorVolume, int virtualDelaySamples, bool loop, string? playbackKey)
        {
            _samples = samples;
            _sampleCount = sampleCount;
            _hasMonitor = hasMonitor;
            if (samples is null) _stream = new ReadAheadSound(path, format, hasMonitor);
            _sampleRate = sampleRate;
            _channels = channels;
            _virtualVolume = virtualVolume;
            _monitorVolume = monitorVolume;
            _initialVirtualDelaySamples = Math.Max(0, virtualDelaySamples);
            _remainingVirtualDelaySamples = _initialVirtualDelaySamples;
            IsLoop = loop;
            PlaybackKey = playbackKey;
        }

        public bool IsLoop { get; private set; }
        public string? PlaybackKey { get; }
        public bool IsFinished => Error is not null || _sampleCount == 0 || (!IsLoop && _virtualFinished && (!_hasMonitor || _monitorFinished));
        public bool IsPaused { get; private set; }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
        }

        public void SetLoop(bool loop)
        {
            IsLoop = loop;
        }

        public void UpdateVolumes(float virtualVolume, float monitorVolume)
        {
            _virtualVolume = virtualVolume;
            _monitorVolume = monitorVolume;
        }

        public void Seek(double seconds)
        {
            var sampleFrame = (long)Math.Round(Math.Max(0, seconds) * _sampleRate);
            var sampleIndex = Math.Clamp(sampleFrame * _channels, 0, _sampleCount);
            _monitorPosition = sampleIndex;
            _virtualPosition = sampleIndex;
            _remainingVirtualDelaySamples = _initialVirtualDelaySamples;
            _monitorFinished = _monitorPosition >= _sampleCount;
            _virtualFinished = _virtualPosition >= _sampleCount;
        }

        public SoundboardStatus GetStatus()
        {
            var durationSeconds = _sampleCount / (double)_channels / _sampleRate;
            var currentSeconds = Math.Min(durationSeconds, (_hasMonitor ? _monitorPosition : _virtualPosition) / (double)_channels / _sampleRate);
            return new SoundboardStatus(true, IsPaused, currentSeconds, durationSeconds);
        }

        public int Read(AudioRoute route, float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            if (IsPaused || (route == AudioRoute.Monitor && !_hasMonitor)) return 0;
            if (_sampleCount == 0) { _virtualFinished = _monitorFinished = true; return 0; }
            var written = 0;
            if (route == AudioRoute.VirtualMicrophone && _remainingVirtualDelaySamples > 0)
            {
                var delay = Math.Min(count, _remainingVirtualDelaySamples);
                _remainingVirtualDelaySamples -= delay;
                offset += delay; count -= delay; written += delay;
            }
            var position = route == AudioRoute.VirtualMicrophone ? _virtualPosition : _monitorPosition;
            var volume = route == AudioRoute.VirtualMicrophone ? _virtualVolume : _monitorVolume;
            var copied = 0;
            while (copied < count)
            {
                if (position >= _sampleCount) { if (!IsLoop) break; position = 0; }
                var wanted = (int)Math.Min(count - copied, _sampleCount - position);
                int read;
                if (_samples is not null)
                {
                    read = wanted;
                    Array.Copy(_samples, position, buffer, offset + copied, read);
                }
                else
                {
                    read = _stream!.Read(route, position, buffer, offset + copied, wanted);
                    if (read == 0)
                    {
                        if (_stream.Ended(route)) position = _sampleCount;
                        break; // Underrun: silence without advancing the source cursor.
                    }
                }
                for (var i = 0; i < read; i++) buffer[offset + copied + i] *= volume;
                copied += read; position += read;
            }
            if (route == AudioRoute.VirtualMicrophone)
            { _virtualPosition = position; _virtualFinished = position >= _sampleCount && _remainingVirtualDelaySamples == 0; }
            else { _monitorPosition = position; _monitorFinished = position >= _sampleCount; }
            return written + copied;
        }
    }
}

public readonly record struct SoundboardStatus(bool IsActive, bool IsPaused, double CurrentSeconds, double DurationSeconds)
{
    public static SoundboardStatus Empty { get; } = new(false, false, 0, 0);
}
