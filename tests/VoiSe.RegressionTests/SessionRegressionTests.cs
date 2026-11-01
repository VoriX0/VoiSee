using VoiSe.App;
using VoiSe.Audio;

internal static class SessionRegressionTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static void Run(Action<string, Action> test)
    {
        test("session owns startup, configures before capture and stops exactly once", () =>
        {
            var queue = new Queue<Action>(); var session = new FakeSession();
            using var controller = new AudioSessionController<FakeSession>(queue.Enqueue, error => throw error);
            Check(controller.State == AudioSessionState.Stopped, "Initial state");
            controller.Start(() => session, item => item.Configured = true);
            Check(session.ConfiguredOnStart && controller.Current == session && controller.State == AudioSessionState.Running, "Start order");
            Check(!controller.Start(() => throw new Exception("Duplicate factory"), _ => { }), "Duplicate start");
            controller.Stop(); controller.Stop(); controller.Dispose(); controller.Dispose();
            Check(session.DisposeCount == 1 && session.Subscribers == 0 && controller.Current is null, "Session retained");
            Check(Throws<ObjectDisposedException>(() => controller.Start(() => new(), _ => { })), "Start after disposal");
        });
        test("failed session creation and startup release ownership and permit retry", () =>
        {
            var queue = new Queue<Action>(); var cleanups = new List<Exception>();
            using var controller = new AudioSessionController<FakeSession>(queue.Enqueue, cleanups.Add);
            Check(Throws<IOException>(() => controller.Start(() => throw new IOException("Device unavailable"), _ => { })), "Factory failure hidden");
            Check(controller.State == AudioSessionState.Faulted && controller.Current is null, "Factory rollback");
            var failed = new FakeSession { FailStart = true, FailDispose = true };
            Check(Throws<IOException>(() => controller.Start(() => failed, _ => { })), "Original start error lost");
            Check(failed.DisposeCount == 1 && failed.Subscribers == 0 && cleanups.Count == 1, "Failed cleanup");
            var retry = new FakeSession(); controller.Start(() => retry, _ => { });
            while (queue.TryDequeue(out var callback)) callback();
            Check(controller.Current == retry && controller.State == AudioSessionState.Running, "Old startup fault stopped retry");
        });
        test("configuration failure rolls back without starting capture", () =>
        {
            using var controller = new AudioSessionController<FakeSession>(_ => { }, error => throw error);
            var session = new FakeSession();
            Check(Throws<IOException>(() => controller.Start(() => session, _ => throw new IOException("Settings failed"))), "Configure error");
            Check(session.Starts == 0 && session.DisposeCount == 1 && session.Subscribers == 0, "Configuration ownership");
        });
        test("native faults are dispatched and stale callbacks cannot stop a replacement session", () =>
        {
            var queue = new System.Collections.Concurrent.ConcurrentQueue<Action>(); var reports = 0;
            using var controller = new AudioSessionController<FakeSession>(queue.Enqueue, error => throw error);
            controller.Faulted += _ => { Check(controller.Current is null && controller.State == AudioSessionState.Faulted, "Fault published before cleanup"); reports++; };
            var old = new FakeSession(); controller.Start(() => old, _ => { });
            Task.Run(() => old.Fail("Disconnected")).GetAwaiter().GetResult();
            Check(controller.Current == old && old.DisposeCount == 0, "Disposed from native callback");
            controller.Stop(); var next = new FakeSession(); controller.Start(() => next, _ => { });
            while (queue.TryDequeue(out var callback)) callback();
            Check(controller.Current == next && reports == 0, "Stale fault affected replacement");
            next.Fail("Render failed"); next.Fail("Repeated");
            while (queue.TryDequeue(out var callback)) callback();
            Check(reports == 1 && next.DisposeCount == 1 && controller.LastError == "Render failed", "Repeated fault cleanup");
        });
        test("lifecycle rejects commands from worker threads and reentrant startup", () =>
        {
            using var controller = new AudioSessionController<FakeSession>(_ => { }, error => throw error);
            Check(Task.Run(() => Throws<InvalidOperationException>(controller.Stop)).GetAwaiter().GetResult(), "Cross-thread stop accepted");
            var session = new FakeSession();
            controller.Start(() => session, _ => Check(Throws<InvalidOperationException>(controller.Stop), "Reentrant stop accepted"));
            Check(controller.Current == session, "Reentrant stop corrupted session");
        });
        test("repeated session replacements do not retain native event subscriptions", () =>
        {
            var queue = new Queue<Action>();
            using var controller = new AudioSessionController<FakeSession>(queue.Enqueue, error => throw error);
            var sessions = new List<FakeSession>();
            for (var i = 0; i < 1000; i++)
            {
                var session = new FakeSession(); sessions.Add(session);
                controller.Start(() => session, _ => { }); session.Fail("Queued old fault"); controller.Stop();
            }
            while (queue.TryDequeue(out var callback)) callback();
            Check(sessions.All(s => s.DisposeCount == 1 && s.Subscribers == 0) && controller.Current is null, "Lifecycle leak");
        });
        test("device refresh preserves stable IDs and refuses an ordinary output as virtual cable", () =>
        {
            var a = Device("mic-a", "Same microphone"); var b = Device("mic-b", "Same microphone");
            var speakers = Device("speakers", "Realtek"); var cable = Device("cable", "CABLE Input (VB-Audio Virtual Cable)");
            var prefs = new AudioDevicePreferences(new("MIC-B", a.FriendlyName), new(speakers.Id, speakers.FriendlyName), new(speakers.Id, speakers.FriendlyName));
            var selected = AudioDeviceSelectionPolicy.Select(new[] { a, b }, new[] { speakers, cable }, prefs);
            Check(selected.Input == b && selected.VirtualOutput == cable && selected.Monitor == speakers, "ID or cable selection");
            var noCable = AudioDeviceSelectionPolicy.Select(new[] { a }, new[] { speakers }, prefs);
            Check(noCable.VirtualOutput is null, "Audio sent to physical speakers as virtual microphone");
        });
        test("manual refresh never substitutes a disconnected input or monitor with a namesake", () =>
        {
            var replacement = Device("new-mic", "Same microphone"); var output = Device("new-output", "Realtek");
            var prefs = new AudioDevicePreferences(new("old-mic", replacement.FriendlyName), new(null, null), new("old-output", output.FriendlyName));
            var selected = AudioDeviceSelectionPolicy.Select(new[] { replacement }, new[] { output }, prefs, "old-mic", "old-output");
            Check(selected.Input is null && selected.Monitor is null, "Refresh switched live route without consent");
            selected = AudioDeviceSelectionPolicy.Select(new[] { replacement }, new[] { output }, prefs);
            Check(selected.Input == replacement && selected.Monitor == output, "Startup fallback lost");
            selected = AudioDeviceSelectionPolicy.Select(Array.Empty<AudioDeviceInfo>(), Array.Empty<AudioDeviceInfo>(), prefs);
            Check(selected == new AudioDeviceSelection(null, null, null), "Empty snapshot");
        });
    }

    private static AudioDeviceInfo Device(string id, string name) => new(id, name, "", "Active");
    private sealed class FakeSession : IAudioSession
    {
        private Action<string>? _faulted;
        public event Action<string>? Faulted { add => _faulted += value; remove => _faulted -= value; }
        public int Subscribers => _faulted?.GetInvocationList().Length ?? 0;
        public bool Configured, ConfiguredOnStart, FailStart, FailDispose;
        public int Starts, DisposeCount;
        public void Start()
        {
            Starts++; ConfiguredOnStart = Configured;
            if (FailStart) { Fail("Startup fault"); throw new IOException("Startup failed"); }
        }
        public void Fail(string message) => _faulted?.Invoke(message);
        public void Dispose() { DisposeCount++; Fail("Final capture callback"); if (FailDispose) throw new IOException("Cleanup failed"); }
    }
}
