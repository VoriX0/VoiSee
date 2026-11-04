using NAudio.Wave;

namespace VoiSe.Audio;

/// <summary>Bounded duration cache. Replaced files are re-read; decoder failures remain retryable.</summary>
public sealed class AudioMetadataCache
{
    private readonly int _capacity;
    private readonly Func<string, double> _readDuration;
    private readonly Dictionary<string, (long Length, long Stamp, double Duration)> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();
    public AudioMetadataCache(int capacity = 512, Func<string, double>? readDuration = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _readDuration = readDuration ?? (path => { using var reader = AudioSourceReader.Open(path); return reader.TotalTime.TotalSeconds; });
    }
    public int Count => _items.Count;
    public double GetDuration(string path)
    {
        path = Path.GetFullPath(path);
        var info = new FileInfo(path);
        if (!info.Exists) { Remove(path); return 0; }
        if (_items.TryGetValue(path, out var value) && value.Length == info.Length && value.Stamp == info.LastWriteTimeUtc.Ticks)
            return value.Duration;
        var duration = _readDuration(path);
        if (!double.IsFinite(duration) || duration < 0) throw new InvalidDataException("Invalid audio duration.");
        if (!_items.ContainsKey(path))
        {
            while (_items.Count >= _capacity && _order.TryDequeue(out var oldest)) _items.Remove(oldest);
            _order.Enqueue(path);
        }
        _items[path] = (info.Length, info.LastWriteTimeUtc.Ticks, duration);
        return duration;
    }
    public void Remove(string path)
    {
        path = Path.GetFullPath(path);
        if (!_items.Remove(path)) return;
        // Keep the eviction queue bounded under repeated replacements/deletions.
        var retained = _order.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToArray();
        _order.Clear(); foreach (var item in retained) _order.Enqueue(item);
    }
}
