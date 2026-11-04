namespace VoiSe.App;

/// <summary>Cancels queued work and waits for admitted operations before editor resources are deleted.</summary>
internal sealed class BackgroundOperationScope : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly SemaphoreSlim _slots = new(2, 2);
    private readonly object _sync = new();
    private readonly HashSet<Task> _tasks = new();
    private bool _closed;
    public BackgroundOperationScope(CancellationToken parent = default) => _cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent);
    public CancellationToken Token => _cancellation.Token;

    public Task<T> Run<T>(Func<CancellationToken, T> operation)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var task = RunCore(operation, _cancellation.Token);
            _tasks.Add(task);
            _ = task.ContinueWith(completed => { lock (_sync) _tasks.Remove(completed); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }
    private async Task<T> RunCore<T>(Func<CancellationToken, T> operation, CancellationToken token)
    {
        await _slots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var result = await Task.Run(() => operation(token), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
        finally { _slots.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_sync) { if (_closed) return; _closed = true; pending = _tasks.ToArray(); }
        _cancellation.Cancel();
        try { await Task.WhenAll(pending).ConfigureAwait(false); } catch { /* Each caller observes its own error. */ }
        _slots.Dispose(); _cancellation.Dispose();
    }
}
