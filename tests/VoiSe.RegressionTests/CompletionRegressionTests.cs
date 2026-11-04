using NAudio.Wave;
using System.Text.Json;
using VoiSe.App;
using VoiSe.Audio;
using VoiSe.Core;

internal static class CompletionRegressionTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
    public static void Run(Action<string, Action> test, string root)
    {
        var directory = Path.Combine(root, "completion"); Directory.CreateDirectory(directory);
        test("device notifications ignore volume traffic and stop after disposal", () =>
        {
            var events = new List<bool>(); using var watcher = new AudioDeviceWatcher(events.Add, subscribe: false);
            watcher.OnDeviceAdded("mic"); watcher.OnDeviceStateChanged("mic", NAudio.CoreAudioApi.DeviceState.Unplugged);
            watcher.OnDefaultDeviceChanged(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Communications, "out");
            watcher.OnDefaultDeviceChanged(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia, "out");
            watcher.OnPropertyValueChanged("out", default); watcher.Dispose(); watcher.OnDeviceRemoved("mic");
            Check(events.SequenceEqual(new[] { false, false, true }), "Unbounded or late device notification");
        });
        test("sleep resume only permits the exact previous route", () =>
        {
            var mic = new AudioDeviceInfo("mic", "Same name", "Capture", "Active");
            var cable = new AudioDeviceInfo("cable", "CABLE Input", "Render", "Active");
            var selection = new AudioDeviceSelection(mic, cable, null);
            var resume = AudioResumePlan.Capture(selection, running: true)!;
            Check(resume.Matches(selection) && !resume.Matches(selection with { Input = mic with { Id = "other" } }), "Resume switched microphone");
            Check(!resume.Matches(selection with { Input = null }) && AudioResumePlan.Capture(selection, running: false) is null, "Resume started a stopped session");
        });
        test("rack state restores defensive snapshots including bypass", () =>
        {
            var state = new VoiceChainState();
            state.Restore(new VoicePreset { SchemaVersion = 3, EffectOrder = new() { "Robot", "Pitch" },
                Sliders = new() { ["Robot"] = 40, ["Pitch"] = 10 }, BypassedEffects = new() { "Robot" }, ChainBypassed = true });
            var captured = state.Capture("Voice"); captured.EffectOrder.Clear(); captured.Sliders["Pitch"] = 80;
            Check(state.Order.SequenceEqual(new[] { "Robot", "Pitch" }) && state.Values["Pitch"] == 10 && state.Bypassed, "Rack snapshot alias");
        });
        test("effect catalog has stable unique IDs and every DSP kind", () =>
        {
            Check(VoiceEffectCatalog.All.Count == Enum.GetValues<VoiceEffectKind>().Length, "Missing effect");
            Check(VoiceEffectCatalog.Ids.Distinct().Count() == VoiceEffectCatalog.All.Count, "Duplicate ID");
            Check(VoiceEffectCatalog.Get("Pitch").Minimum == -100 && VoiceEffectCatalog.Get("Compressor").Maximum == 200, "Range changed");
        });
        test("legacy presets migrate values and preserve their original activation rules", () =>
        {
            var preset = new VoicePreset { SchemaVersion = 1, Sliders = new() { ["Pitch"] = 25, ["Gate"] = -100, ["Compressor"] = -50, ["Robot"] = 0 } };
            var snapshot = VoicePresetSnapshot.Copy(preset);
            Check(snapshot.EffectOrder.SequenceEqual(new[] { "Compressor", "Pitch" }) && snapshot.Sliders["Compressor"] == 50, "Legacy conversion");
            Check(preset.SchemaVersion == 1 && preset.Sliders["Compressor"] == -50, "Input mutated");
        });
        test("scene voice snapshots survive preset changes and include bypass and order", () =>
        {
            var preset = new VoicePreset { Id = "stable", Name = "Original", EffectOrder = new() { "Pitch", "Robot" },
                Sliders = new() { ["Pitch"] = 20, ["Robot"] = 35 }, BypassedEffects = new() { "Robot" }, ChainBypassed = true };
            var scene = new VoiSeScene(); SceneVoiceState.Capture(scene, preset);
            preset.Name = "Renamed"; preset.Sliders["Pitch"] = 80; preset.EffectOrder.Reverse();
            var snapshot = SceneVoiceState.Resolve(scene, Array.Empty<VoicePreset>())!;
            Check(snapshot.EffectOrder.SequenceEqual(new[] { "Pitch", "Robot" }) && snapshot.Sliders["Pitch"] == 20, "Scene changed with preset");
            Check(snapshot.ChainBypassed && snapshot.BypassedEffects.SequenceEqual(new[] { "Robot" }), "Bypass lost");
            Check(SceneVoiceState.FindPreset(scene, new[] { preset }) == preset, "Rename broke ID link");
            snapshot.Sliders.Clear(); Check(scene.VoiceSliders.Count == 2, "Snapshot alias");
            SceneVoiceState.Clear(scene); Check(SceneVoiceState.Resolve(scene, new[] { preset })!.EffectOrder.Count == 0, "Cleared scene resurrected preset");
        });
        test("preset IDs persist across legacy loading, renaming and reloading", () =>
        {
            var store = new VoicePresetStore(Path.Combine(directory, "presets"));
            var path = Path.Combine(store.PresetsDirectory, "legacy.json");
            File.WriteAllText(path, "{\"SchemaVersion\":2,\"Name\":\"Legacy\",\"Sliders\":{\"Pitch\":20},\"EffectOrder\":[\"Pitch\"]}");
            var preset = store.LoadPresets().Single(); var id = preset.Id;
            Check(!string.IsNullOrWhiteSpace(id) && store.LoadPresets().Single().Id == id, "Unstable legacy ID");
            store.RenamePreset(preset, "Renamed");
            Check(store.LoadPresets().Single().Id == id && !File.Exists(path), "Rename identity or file");
        });
        test("failed preset rename preserves previous JSON and model name", () =>
        {
            var store = new VoicePresetStore(Path.Combine(directory, "locked-preset"));
            var preset = new VoicePreset { Name = "Before", EffectOrder = new() { "Pitch" }, Sliders = new() { ["Pitch"] = 20 } };
            store.SavePreset(preset); var originalPath = preset.FilePath!; var original = File.ReadAllBytes(originalPath);
            using (var handle = new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(Throws<IOException>(() => store.RenamePreset(preset, "After")), "Locked old file was deleted");
            Check(preset.Name == "Before" && preset.FilePath == originalPath && File.ReadAllBytes(originalPath).SequenceEqual(original), "Rename lost original");
            Check(store.LoadPresets().Count == 1, "Failed rename left duplicate");
        });
        test("scene snapshot schema roundtrips without changing the global equalizer", () =>
        {
            var store = new SceneStore(Path.Combine(directory, "scenes")); var scene = new VoiSeScene();
            SceneVoiceState.Capture(scene, new VoicePreset { EffectOrder = new() { "Robot", "Pitch" }, Sliders = new() { ["Robot"] = 40, ["Pitch"] = 10 } });
            store.SaveScene(scene); var loaded = store.LoadScenes().Single();
            Check(loaded.SchemaVersion >= 10 && loaded.HasVoiceSnapshot && loaded.VoiceEffectOrder.SequenceEqual(new[] { "Robot", "Pitch" }), "Scene schema");
            Check(!File.ReadAllText(loaded.FilePath!).Contains("Equalizer"), "Scene owns user EQ");
        });
        test("failed atomic audio commit preserves target and leaves no temporary file", () =>
        {
            var path = Path.Combine(directory, "atomic.wav"); File.WriteAllText(path, "original");
            using (var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(Throws<IOException>(() => AtomicFileTransaction.Write(path, temp => File.WriteAllText(temp, "replacement"))), "Locked target replaced");
            Check(File.ReadAllText(path) == "original" && !Directory.EnumerateFiles(directory, "atomic.wav.*.tmp").Any(), "Atomic rollback");
            using var cancel = new CancellationTokenSource();
            Check(Throws<OperationCanceledException>(() => AtomicFileTransaction.Write(path, temp => { File.WriteAllText(temp, "replacement"); cancel.Cancel(); }, cancel.Token)), "Cancellation committed");
            Check(File.ReadAllText(path) == "original", "Cancelled commit changed original");
        });
        var wave = Path.Combine(directory, "source.wav");
        using (var writer = new WaveFileWriter(wave, new WaveFormat(48000, 16, 1)))
        {
            var samples = Enumerable.Range(0, 48000).Select(i => .1f * (float)Math.Sin(i * 2 * Math.PI * 220 / 48000)).ToArray();
            for (var i = 0; i < 4; i++) writer.WriteSamples(samples, 0, samples.Length);
        }
        test("editor waveform is bounded and cancellation releases the decoder", () =>
        {
            var peaks = AudioWaveform.Read(wave, 64);
            Check(peaks.Length is > 0 and <= 64 && peaks.All(float.IsFinite), "Waveform bounds");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Check(Throws<OperationCanceledException>(() => AudioWaveform.Read(wave, 64, cancel.Token)), "Waveform ignores cancellation");
            using var exclusive = new FileStream(wave, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });
        test("audio rendering commits valid WAV and cancellation preserves previous output", () =>
        {
            var path = Path.Combine(directory, "render.wav");
            var request = new SoundEditRequest { SourcePath = wave, TargetPath = path, TrimStartSeconds = .5, TrimEndSeconds = 1.5, GainDb = -3 };
            SoundEditProcessor.RenderToWav(request); using (var reader = new WaveFileReader(path)) Check(Math.Abs(reader.TotalTime.TotalSeconds - 1) < .001, "Render duration");
            var before = File.ReadAllBytes(path); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Check(Throws<OperationCanceledException>(() => SoundEditProcessor.RenderToWav(request, cancel.Token)), "Render cancellation");
            Check(File.ReadAllBytes(path).SequenceEqual(before), "Cancelled render overwrote output");
        });
        test("metadata cache invalidates file revisions, retries errors and stays bounded", () =>
        {
            var calls = 0; var fail = false;
            var cache = new AudioMetadataCache(2, _ => { calls++; if (fail) throw new IOException("retry"); return calls; });
            var path = Path.Combine(directory, "metadata"); File.WriteAllText(path, "1");
            var first = cache.GetDuration(path); Check(cache.GetDuration(path) == first && calls == 1, "Metadata hit");
            var stamp = File.GetLastWriteTimeUtc(path); File.WriteAllText(path, "longer"); File.SetLastWriteTimeUtc(path, stamp);
            Check(cache.GetDuration(path) != first, "Same timestamp, new size not detected");
            cache.Remove(path); fail = true; Check(Throws<IOException>(() => cache.GetDuration(path)), "Metadata error swallowed");
            fail = false; cache.GetDuration(path);
            for (var i = 0; i < 12; i++) { var next = path + i; File.WriteAllText(next, "1"); cache.GetDuration(next); }
            Check(cache.Count == 2, "Unbounded metadata");
        });
        test("failed persistence stays visible and retry uses latest state", () =>
        {
            var coordinator = new PersistenceCoordinator(); var fail = true; var value = 1; var saved = 0;
            void Write() { if (fail) throw new IOException("disk unavailable"); saved = value; }
            Check(!coordinator.Save("settings", Write) && coordinator.PendingCount == 1 && coordinator.LastError is not null, "Save falsely reported success");
            value = 2; coordinator.Save("settings", Write); Check(coordinator.PendingCount == 1, "Duplicate pending save");
            fail = false; coordinator.Retry(); Check(saved == 2 && coordinator.PendingCount == 0 && coordinator.LastError is null, "Retry stale or failed");
        });
        test("editor lifetime cancels queued work and drains active operations before disposal", () =>
        {
            var scope = new BackgroundOperationScope(); var active = 0; var maximum = 0;
            var tasks = Enumerable.Range(0, 12).Select(_ => scope.Run(token =>
            {
                var count = Interlocked.Increment(ref active); lock (scope) maximum = Math.Max(maximum, count);
                try { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return 0; }
                finally { Interlocked.Decrement(ref active); }
            })).ToArray();
            SpinWait.SpinUntil(() => Volatile.Read(ref active) == 2, 2000);
            scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(active == 0 && maximum <= 2 && tasks.All(t => t.IsCanceled), "Late work or unbounded concurrency");
            Check(Throws<ObjectDisposedException>(() => scope.Run(_ => 0)), "Disposed scope accepts work");
        });
        test("queued playback is cancelled by Stop before workers can resurrect it", () =>
        {
            using var transport = new SoundboardTransport(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
            var tasks = Enumerable.Range(0, 40).Select(i => transport.PlayAsync(wave, 1, 0, 0, playbackKey: "queued-"+i)).ToArray();
            transport.Stop();
            foreach (var task in tasks) { try { task.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } }
            Check(!transport.IsActive && tasks.Any(t => t.IsCanceled), "Stop was followed by late playback");
            transport.PlayAsync(wave, 1, 0, 0).GetAwaiter().GetResult(); Check(transport.IsActive, "Retry after Stop");
        });
        test("failed asynchronous decode releases pending request and admits retry", () =>
        {
            using var transport = new SoundboardTransport(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
            var missing = Path.Combine(directory, "missing.wav");
            Check(Throws<FileNotFoundException>(() => transport.PlayAsync(missing, 1, 0, 0, playbackKey: "same").GetAwaiter().GetResult()), "Decode failure hidden");
            transport.PlayAsync(wave, 1, 0, 0, playbackKey: "same").GetAwaiter().GetResult(); Check(transport.IsActive, "Failed request retained");
        });
    }
}
