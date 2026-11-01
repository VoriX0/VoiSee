using VoiSe.Audio;

namespace VoiSe.App;

internal enum AudioSessionState { Stopped, Starting, Running, Stopping, Faulted, Disposed }

/// <summary>
/// Owns one session on the application's dispatcher thread. Native callbacks only
/// enqueue notifications: they never wait for the UI or dispose their own capture.
/// </summary>
internal sealed class AudioSessionController<TSession> : IDisposable where TSession : class, IAudioSession
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Action<Action> _post;
    private readonly Action<Exception> _reportCleanupError;
    private Action<string>? _faultHandler;
    private long _generation;

    public AudioSessionController(Action<Action> post, Action<Exception> reportCleanupError)
    {
        _post = post;
        _reportCleanupError = reportCleanupError;
    }

    public TSession? Current { get; private set; }
    public AudioSessionState State { get; private set; }
    public string? LastError { get; private set; }
    public event Action<string>? Faulted;

    public bool Start(Func<TSession> create, Action<TSession> configure)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(State == AudioSessionState.Disposed, this);
        if (State == AudioSessionState.Running) return false;
        if (State is AudioSessionState.Starting or AudioSessionState.Stopping)
            throw new InvalidOperationException("An audio lifecycle operation is already in progress.");

        State = AudioSessionState.Starting;
        LastError = null;
        var generation = ++_generation;
        try
        {
            var session = create();
            Current = session;
            _faultHandler = error => _post(() => HandleFault(generation, session, error));
            session.Faulted += _faultHandler;
            // Set mute and route gains before capture/render starts.
            configure(session);
            session.Start();
            State = AudioSessionState.Running;
            return true;
        }
        catch (Exception error)
        {
            ReleaseCurrent();
            LastError = error.Message;
            State = AudioSessionState.Faulted;
            throw;
        }
    }

    public void Stop()
    {
        VerifyAccess();
        if (State == AudioSessionState.Disposed) return;
        if (State is AudioSessionState.Starting or AudioSessionState.Stopping)
            throw new InvalidOperationException("An audio lifecycle operation is already in progress.");
        State = AudioSessionState.Stopping;
        ReleaseCurrent();
        State = AudioSessionState.Stopped;
        LastError = null;
    }

    private void HandleFault(long generation, TSession session, string error)
    {
        VerifyAccess();
        if (generation != _generation || !ReferenceEquals(Current, session)) return;
        State = AudioSessionState.Stopping;
        ReleaseCurrent();
        LastError = error;
        State = AudioSessionState.Faulted;
        Faulted?.Invoke(error);
    }

    private void ReleaseCurrent()
    {
        ++_generation;
        var session = Current;
        var handler = _faultHandler;
        Current = null;
        _faultHandler = null;
        if (session is null) return;
        // Detach before Dispose; StopRecording may itself issue a final callback.
        if (handler is not null) session.Faulted -= handler;
        try { session.Dispose(); }
        catch (Exception error) { _reportCleanupError(error); }
    }

    public void Dispose()
    {
        VerifyAccess();
        if (State == AudioSessionState.Disposed) return;
        Stop();
        State = AudioSessionState.Disposed;
        Faulted = null;
    }

    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Audio lifecycle commands must run on the owner dispatcher thread.");
    }
}
