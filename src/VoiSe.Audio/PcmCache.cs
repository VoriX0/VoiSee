namespace VoiSe.Audio;

// Immutable clips may outlive eviction while a playback still owns them.
// The cache itself never owns more than its budget; decoding is serialized by the loader.
internal sealed class PcmCache
{
    private readonly long _budget;
    private readonly Dictionary<SoundCacheKey, LinkedListNode<(SoundCacheKey Key, float[] Samples)>> _items = new();
    private readonly LinkedList<(SoundCacheKey Key, float[] Samples)> _lru = new();
    private readonly object _sync = new();
    private long _bytes;
    public PcmCache(long budget) => _budget = budget;
    public long RetainedBytes { get { lock (_sync) return _bytes; } }
    public float[]? Get(SoundCacheKey key)
    {
        lock (_sync)
        {
            if (!_items.TryGetValue(key, out var node)) return null;
            _lru.Remove(node); _lru.AddFirst(node); return node.Value.Samples;
        }
    }
    public void Put(SoundCacheKey key, float[] samples)
    {
        lock (_sync)
        {
            Invalidate(key.FilePath);
            var bytes = samples.LongLength * sizeof(float);
            if (bytes > _budget) return;
            while (_bytes + bytes > _budget && _lru.Last is { } last) Remove(last);
            var node = _lru.AddFirst((key, samples)); _items.Add(key, node); _bytes += bytes;
        }
    }
    public void Invalidate(string path)
    {
        lock (_sync)
        {
            for (var node = _lru.First; node is not null;)
            {
                var next = node.Next;
                if (node.Value.Key.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)) Remove(node);
                node = next;
            }
        }
    }
    public void Clear() { lock (_sync) { _items.Clear(); _lru.Clear(); _bytes = 0; } }
    private void Remove(LinkedListNode<(SoundCacheKey Key, float[] Samples)> node)
    {
        _items.Remove(node.Value.Key); _lru.Remove(node); _bytes -= node.Value.Samples.LongLength * sizeof(float);
    }
}
