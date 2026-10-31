using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiSe.Audio;

// Each output has an independent bounded decoder cursor (virtual output may be delayed).
// File access/resampling belongs exclusively to the worker, never to a WASAPI Read.
internal sealed class ReadAheadSound : IDisposable
{
    private readonly Reader _virtual;
    private readonly Reader? _monitor;
    public ReadAheadSound(string path, WaveFormat format, bool monitor)
    {
        _virtual = new Reader(path, format);
        if (monitor) _monitor = new Reader(path, format);
    }
    public int Read(AudioRoute route, long position, float[] buffer, int offset, int count)
        => (route == AudioRoute.VirtualMicrophone ? _virtual : _monitor)?.Read(position, buffer, offset, count) ?? 0;
    public bool Ended(AudioRoute route) => (route == AudioRoute.VirtualMicrophone ? _virtual : _monitor)?.Ended ?? true;
    public string? Error => _virtual.Error ?? _monitor?.Error;
    public void Dispose() { _virtual.Dispose(); _monitor?.Dispose(); }

    private sealed class Reader : IDisposable
    {
        private readonly object _sync = new();
        private readonly FloatSampleQueue _queue;
        private readonly SemaphoreSlim _wake = new(0, 1);
        private readonly int _capacity;
        private long _position, _seek;
        private int _generation;
        private bool _seekPending = true, _eof, _disposed;
        private string? _error;
        public bool Ended { get { lock (_sync) return _eof && _queue.Count == 0; } }
        public string? Error { get { lock (_sync) return _error; } }
        public Reader(string path, WaveFormat format)
        {
            _capacity = format.SampleRate * format.Channels / 4; // 250 ms per output
            _queue = new FloatSampleQueue(_capacity);
            _ = Task.Run(() => Run(path, format)); // Run observes all decoder failures.
        }
        public int Read(long position, float[] buffer, int offset, int count)
        {
            lock (_sync)
            {
                if (_disposed) { Array.Clear(buffer, offset, count); return 0; }
                if (position != _position)
                {
                    _position = position; _seek = position; _generation++;
                    _seekPending = true; _eof = false; _queue.Clear();
                }
                var read = _queue.Read(buffer, offset, count);
                _position += read;
                Wake();
                return read;
            }
        }
        private void Wake() { if (_wake.CurrentCount == 0) _wake.Release(); }
        private async Task Run(string path, WaveFormat target)
        {
            try
            {
                using var stream = SoundFileLoader.CreateReader(path);
                ISampleProvider MakeProvider() => stream.WaveFormat.SampleRate == target.SampleRate
                    ? stream.ToSampleProvider() : new WdlResamplingSampleProvider(stream.ToSampleProvider(), target.SampleRate);
                var provider = MakeProvider();
                var channels = provider.WaveFormat.Channels;
                const int frames = 2048;
                var input = new float[frames * channels];
                var output = new float[frames * target.Channels];
                while (true)
                {
                    int generation; long seek; bool reset;
                    lock (_sync)
                    {
                        if (_disposed) return;
                        generation = _generation; seek = _seek; reset = _seekPending;
                        _seekPending = false;
                        if (!reset && (_eof || _queue.Count + output.Length > _capacity)) generation = -1;
                    }
                    if (generation < 0) { await _wake.WaitAsync().ConfigureAwait(false); continue; }
                    if (reset)
                    {
                        stream.CurrentTime = TimeSpan.FromSeconds(seek / (double)target.Channels / target.SampleRate);
                        provider = MakeProvider();
                    }
                    var samples = provider.Read(input, 0, input.Length);
                    var frameCount = samples / channels;
                    for (var f = 0; f < frameCount; f++)
                        for (var c = 0; c < target.Channels; c++)
                            output[f * target.Channels + c] = input[f * channels + Math.Min(c, channels - 1)];
                    lock (_sync)
                    {
                        if (_disposed) return;
                        if (generation != _generation) continue;
                        _queue.Add(output.AsSpan(0, frameCount * target.Channels));
                        _eof = samples == 0;
                    }
                }
            }
            catch (Exception ex) { lock (_sync) { _error = ex.Message; _queue.Clear(); _eof = true; } }
            finally { lock (_sync) { _disposed = true; _wake.Dispose(); } }
        }
        public void Dispose() { lock (_sync) { if (_disposed) return; _disposed = true; _queue.Clear(); Wake(); } }
    }
}
