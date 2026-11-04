namespace VoiSe.App;

/// <summary>Retains failed saves for explicit retry; a failure is never reported as success.</summary>
internal sealed class PersistenceCoordinator
{
    private readonly Dictionary<string, Action> _pending = new(StringComparer.Ordinal);
    public int PendingCount => _pending.Count;
    public string? LastError { get; private set; }
    public event Action? Changed;
    public bool Save(string key, Action write)
    {
        try { write(); _pending.Remove(key); if (_pending.Count == 0) LastError = null; }
        catch (Exception error) { _pending[key] = write; LastError = error.Message; Changed?.Invoke(); return false; }
        Changed?.Invoke(); return true;
    }
    public void Retry()
    {
        foreach (var (key, write) in _pending.ToArray()) Save(key, write);
    }
}
