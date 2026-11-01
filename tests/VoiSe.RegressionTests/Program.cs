using NAudio.Wave;
using System.Diagnostics;
using System.Text.Json;
using VoiSe.App;
using VoiSe.Audio;

var root = Path.GetFullPath(Path.Combine("artifacts", "regression-tests", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var results = new List<string>();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Test(string name, Action test) { test(); results.Add(name); Console.WriteLine("PASS " + name); }
var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
string Wave(string name, int seconds, int rate = 48000)
{
    var path = Path.Combine(root, name + ".wav");
    using var writer = new WaveFileWriter(path, new WaveFormat(rate, 16, 1));
    var positive = new byte[rate * 2]; var negative = new byte[rate * 2];
    for (var i = 0; i < rate; i++) { positive[i * 2 + 1] = 32; negative[i * 2 + 1] = 224; }
    for (var i = 0; i < seconds; i++) writer.Write(i < seconds / 2 ? positive : negative, 0, positive.Length);
    return path;
}
var shortWave = Wave("short", 4);
Test("localization uses Russian by default and keeps effect IDs and user data stable", () =>
{
    Check(new VoiSeUserSettings().InterfaceLanguage == "ru", "New profile language is not Russian");
    UiText.Initialize(null); Check(UiText.LanguageCode == "ru" && UiText.EffectName("Pitch") == "Высота голоса", "Russian fallback failed");
    UiText.Initialize("en"); Check(UiText.EffectName("Pitch") == "Pitch" && UiText.CategoryName("Special") == "Character", "English resources missing");
    var settings = new VoiSeUserSettings { InterfaceLanguage = "en", InputDeviceId = "original-endpoint", InputDeviceName = "My English microphone", ActiveVoiceEffectOrder = new() { "Pitch", "Gate" } };
    var json = JsonSerializer.Serialize(settings); UiText.Initialize("ru");
    var restored = JsonSerializer.Deserialize<VoiSeUserSettings>(json)!;
    Check(restored.InterfaceLanguage == "en" && restored.InputDeviceId == "original-endpoint" && restored.InputDeviceName == "My English microphone" && restored.ActiveVoiceEffectOrder.SequenceEqual(new[] { "Pitch", "Gate" }), "Language changed persisted identities");
    var sound = new SoundBoardSound { Name = "My English Track", OriginalFileName = "English.wav" };
    Check(sound.DisplayName == "My English Track", "User sound name translated");
    Check(UiText.ErrorMessage("custom error from driver") == "custom error from driver", "Unknown diagnostic was rewritten");
    var scene = new VoiSeScene { Name = "My English Scene", VoicePresetName = "My English Preset" };
    Check(scene.SceneListSubtitle.Contains("Голос: My English Preset") && scene.Name == "My English Scene", "Scene UI was not localized independently of user names");
    UiText.Initialize("unknown"); Check(UiText.LanguageCode == "ru", "Unsupported saved language did not fall back safely");
});
Test("both interface resource catalogs are complete and format safely", () =>
{
    var manager = new System.Resources.ResourceManager("VoiSe.App.Resources.Interface", typeof(UiText).Assembly);
    var russian = manager.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo("ru"), true, true)!;
    var english = manager.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo("en"), true, false)!;
    var ru = russian.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    var en = english.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    Check(ru.Count > 600 && ru.Keys.Order().SequenceEqual(en.Keys.Order()), "Catalog keys differ or resource set is incomplete");
    foreach (var catalog in new[] { ru, en }) foreach (var item in catalog)
    {
        Check(!string.IsNullOrWhiteSpace(item.Value), "Empty resource: " + item.Key);
        if (System.Text.RegularExpressions.Regex.IsMatch(item.Value, @"\{\d"))
            _ = string.Format(System.Globalization.CultureInfo.InvariantCulture, item.Value, Enumerable.Repeat<object>(1.0, 20).ToArray());
    }
    Console.WriteLine("LOCALIZED_INTERFACE_RESOURCES=" + ru.Count);
});
Test("equalizer undo groups gestures, isolates snapshots and ignores unchanged settings", () =>
{
    var settings = new EqualizerSettings(); var history = new EqualizerEditHistory(settings, 2);
    history.Record(settings, 2); Check(history.Count == 0, "No-op recorded");
    history.BeginGesture();
    for (var i = 1; i <= 100; i++) { settings.ParametricBands[2].GainDb = i / 10.0; history.Record(settings, 2); }
    history.EndGesture(); Check(history.Count == 1, "Drag split into intermediate steps");
    var restored = history.Undo()!; Check(restored.Settings.ParametricBands[2].GainDb == 0 && history.Count == 0, "Drag did not restore origin");
    settings = restored.Settings; settings.GainsDb[0] = 3; history.Record(settings, 2);
    Check(history.Undo()!.Settings.GainsDb[0] == 0, "Restored model mutated retained history");
    settings = new EqualizerSettings(); history = new EqualizerEditHistory(settings, 2);
    history.BeginGesture(); settings.PreampDb = 3; history.Record(settings, 2); settings.PreampDb = 0; history.Record(settings, 2); history.EndGesture();
    Check(history.Count == 0, "Returning a drag to its origin retained a no-op");
});
Test("equalizer undo restores structural edits and reset with bounded history", () =>
{
    var settings = new EqualizerSettings(); var history = new EqualizerEditHistory(settings, 2);
    settings.ParametricBands[2].Q = 3; history.Record(settings, 2);
    settings.AddPoint(2000, 4); history.Record(settings, 5);
    settings.RemovePoint(2); history.Record(settings, 2);
    settings = new EqualizerSettings(); history.Record(settings, 2);
    settings = history.Undo()!.Settings;
    Check(settings.ParametricBands.Length == 5 && settings.ParametricBands[^1].Frequency == 2000, "Reset lost deleted-point state");
    settings = history.Undo()!.Settings;
    Check(settings.ParametricBands.Length == 6 && settings.ParametricBands[2].Q == 3, "Deleted point not restored");
    Check(history.Undo()!.Settings.ParametricBands.Length == 5, "Added point not removed");
    Check(history.Undo()!.Settings.ParametricBands[2].Q == 1 && history.Undo() is null, "Prior width edit lost");
    settings = new EqualizerSettings(); history = new EqualizerEditHistory(settings, 2);
    for (var i = 0; i < 150; i++) { settings.Enabled = !settings.Enabled; history.Record(settings, 2); }
    Check(history.Count == EqualizerEditHistory.Capacity, "History exceeded memory bound");
    for (var i = 0; i < EqualizerEditHistory.Capacity; i++) Check(history.Undo() is not null, "History missing entries");
    Check(history.Undo() is null, "History retained entries over capacity");
});
Test("equalizer undo merges repeated keys but separates gestures and parameters", () =>
{
    var settings = new EqualizerSettings(); var history = new EqualizerEditHistory(settings, 2);
    settings.PreampDb = -1; history.Record(settings, 2, "preamp"); settings.PreampDb = -2; history.Record(settings, 2, "preamp");
    Check(history.Count == 1, "Repeated key edits split");
    settings.ParametricBands[2].Q = 2; history.Record(settings, 2, "width"); Check(history.Count == 2, "Different control merged");
    history.BeginGesture(); settings.PreampDb = -3; history.Record(settings, 2, "preamp"); history.EndGesture();
    history.BeginGesture(); settings.PreampDb = -4; history.Record(settings, 2, "preamp"); history.EndGesture();
    Check(history.Count == 4 && history.Undo()!.Settings.PreampDb == -3 && history.Undo()!.Settings.PreampDb == -2, "Separate drags merged");
    settings = history.Undo()!.Settings; settings.PreampDb = -5; history.Record(settings, 2, "preamp");
    Check(history.Undo()!.Settings.PreampDb == -2, "Edit after undo merged into earlier action");
});
Test("ring wraps and drops oldest", () =>
{
    var q = new FloatSampleQueue(4); var b = new float[5];
    q.Add(new float[] { 1, 2, 3 }); q.Read(b, 0, 2); q.Add(new float[] { 4, 5, 6, 7, 8 }); q.Read(b, 0, 5);
    Check(b.SequenceEqual(new float[] { 5, 6, 7, 8, 0 }), "Incorrect ring overflow");
    Check(q.DroppedSampleCount == 2, "Incorrect drop accounting");
});
Test("bounded LRU eviction and invalidation", () =>
{
    var cache = new PcmCache(32);
    var a = new SoundCacheKey("a", 1, 48000, 2); var b = a with { FilePath = "b" }; var c = a with { FilePath = "c" };
    cache.Put(a, new float[4]); cache.Put(b, new float[4]); _ = cache.Get(a); cache.Put(c, new float[4]);
    Check(cache.Get(b) is null && cache.Get(a) is not null, "Wrong LRU victim");
    cache.Put(a with { LastWriteTimeUtcTicks = 2 }, new float[4]);
    Check(cache.Get(a) is null && cache.RetainedBytes == 32, "Old revision retained");
    cache.Clear(); Check(cache.RetainedBytes == 0, "Cache clear failed");
});
Test("loader budget under library pressure", () =>
{
    SoundFileLoader.ClearCache();
    for (var i = 0; i < 50; i++) { var path = Path.Combine(root, "clip-" + i + ".wav"); File.Copy(shortWave, path); _ = SoundFileLoader.LoadToFormat(path, format); }
    Check(SoundFileLoader.CachedBytes <= SoundFileLoader.CacheBudgetBytes, "Cache budget exceeded");
    Console.WriteLine("CACHE_BYTES=" + SoundFileLoader.CachedBytes);
});
Test("one route completes without monitor", () =>
{
    using var t = new SoundboardTransport(format, false); t.Play(shortWave, 1, 0, 0, playbackKey: "one");
    var b = new float[1920]; for (var i = 0; i < 205; i++) t.Read(AudioRoute.VirtualMicrophone, b, 0, b.Length);
    Check(!t.IsActive && !t.GetStatus("one").IsActive, "Finished sound remains active");
});
Test("dual route gains and independent virtual delay", () =>
{
    using var t = new SoundboardTransport(format); t.Play(shortWave, .5f, 1, 100);
    var v = new float[1920]; var m = new float[1920]; t.Read(AudioRoute.VirtualMicrophone, v, 0, v.Length); t.Read(AudioRoute.Monitor, m, 0, m.Length);
    Check(v.All(x => x == 0) && m.All(x => x == .25f), "Initial routing/delay changed");
    for (var i = 0; i < 5; i++) t.Read(AudioRoute.VirtualMicrophone, v, 0, v.Length);
    Check(v.All(x => x == .125f), "Virtual gain changed");
});
Test("steady mixer allocates zero per block with eight voices", () =>
{
    using var t = new SoundboardTransport(format); for (var i = 0; i < 8; i++) t.Play(shortWave, 1, 1, 0, playbackKey: "v" + i);
    var b = new float[1920]; t.Read(AudioRoute.VirtualMicrophone, b, 0, b.Length); t.Read(AudioRoute.Monitor, b, 0, b.Length);
    var start = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 100; i++) { t.Read(AudioRoute.VirtualMicrophone, b, 0, b.Length); t.Read(AudioRoute.Monitor, b, 0, b.Length); }
    var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
    Check(allocated == 0, "Mixer allocations: " + allocated); Console.WriteLine("MIXER_BYTES_PER_BLOCK=0");
});
Test("stop/dispose rejects late sound", () =>
{
    var t = new SoundboardTransport(format); t.Play(shortWave, 1, 1, 0); t.Stop(); Check(!t.IsActive, "Stop retained sounds");
    t.Dispose(); t.Dispose(); var rejected = false;
    try { t.Play(shortWave, 1, 1, 0); } catch (ObjectDisposedException) { rejected = true; }
    Check(rejected, "Disposed transport accepted Play");
});
var longWave = Wave("long", 30);
void WaitForAudio(SoundboardTransport t, float expected)
{
    var b = new float[1920]; var watch = Stopwatch.StartNew();
    while (watch.Elapsed < TimeSpan.FromSeconds(3))
    {
        t.Read(AudioRoute.VirtualMicrophone, b, 0, b.Length);
        // A resampling filter can ring at the initial discontinuity. Inspect the
        // settled tail, not its transient impulse response.
        if (b.TakeLast(128).All(x => Math.Abs(x - expected) < .001f)) return;
        if (b.TakeLast(128).Any(x => x != 0 && Math.Sign(x) != Math.Sign(expected)))
            throw new Exception("Wrong seek/stream sign");
        Thread.Sleep(2);
    }
    throw new Exception("Streaming decoder produced no audio: " + t.LastError);
}
Test("long playback streams and seeks without full PCM cache", () =>
{
    SoundFileLoader.ClearCache(); using var t = new SoundboardTransport(format, false);
    t.Play(longWave, 1, 0, 0); WaitForAudio(t, .25f);
    Check(SoundFileLoader.CachedBytes == 0, "Long file entered PCM cache");
    t.Seek(22); WaitForAudio(t, -.25f);
    Check(t.TogglePause(), "Pause failed"); var b = new float[1920]; t.Read(AudioRoute.VirtualMicrophone, b, 0, b.Length);
    Check(b.All(x => x == 0), "Paused stream emitted audio"); Check(!t.TogglePause(), "Resume failed");
    t.Stop(); Check(!t.IsActive, "Streaming Stop failed");
});
Test("streamed 44.1k source resamples", () =>
{
    var path = Wave("long-44100", 30, 44100); using var t = new SoundboardTransport(format, false);
    t.Play(path, 1, 0, 0); WaitForAudio(t, .25f);
});
Test("streamed loop returns to beginning", () =>
{
    using var t = new SoundboardTransport(format, false);
    t.Play(longWave, 1, 0, 0, loop: true); t.Seek(29.9);
    var block = new float[1920]; var negative = false; var positive = false;
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed < TimeSpan.FromSeconds(3) && !positive)
    {
        t.Read(AudioRoute.VirtualMicrophone, block, 0, block.Length);
        negative |= block.Any(x => x < -.2f);
        positive = negative && block.Any(x => x > .2f);
        Thread.Sleep(2);
    }
    Check(negative && positive && t.IsActive, "Loop did not rewind the streaming reader");
});
Test("cancelled stream releases decoder files", () =>
{
    for (var i = 0; i < 12; i++) { var t = new SoundboardTransport(format); t.Play(longWave, 1, 1, 0); t.Dispose(); }
    var watch = Stopwatch.StartNew();
    while (true)
    {
        try { using var exclusive = new FileStream(longWave, FileMode.Open, FileAccess.ReadWrite, FileShare.None); break; }
        catch (IOException) { if (watch.Elapsed > TimeSpan.FromSeconds(3)) throw; Thread.Sleep(10); }
    }
});
Test("atomic JSON keeps previous version and recovers corruption", () =>
{
    var path = Path.Combine(root, "settings.json"); AtomicFile.WriteAllText(path, "{\"value\":1}"); AtomicFile.WriteAllText(path, "{\"value\":2}");
    File.WriteAllText(path, "broken"); var json = AtomicFile.ReadJson<Dictionary<string, int>>(path, new());
    Check(json!["value"] == 1 && Directory.GetFiles(root, "settings.json.corrupt-*").Length == 1, "Recovery lost original or backup");
});
Test("failed atomic commit leaves original intact", () =>
{
    var path = Path.Combine(root, "locked.json"); AtomicFile.WriteAllText(path, "original"); var failed = false;
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        try { AtomicFile.WriteAllText(path, "replacement"); } catch (IOException) { failed = true; }
    }
    Check(failed && File.ReadAllText(path) == "original", "Failed write destroyed original");
    Check(!Directory.GetFiles(root, "locked.json.*.tmp").Any(), "Temporary file leaked");
});
Test("locked replacement preserves original library audio", () =>
{
    var store = new SoundBoardLibraryStore(Path.Combine(root, "library")); var lib = store.Load(); var sound = store.AddSound(lib, shortWave, lib.Categories[0]);
    var original = sound.FilePath; var other = Wave("replacement", 1); var failed = false;
    using (var held = new FileStream(other, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        try { store.ReplaceSoundFile(lib, sound, other); } catch (IOException) { failed = true; }
    }
    Check(failed && File.Exists(original) && sound.FilePath == original, "Replace removed old track on failure");
    store.ReplaceSoundFile(lib, sound, other); Check(File.Exists(sound.FilePath), "Successful replacement missing");
});
Test("capture converter is allocation-free after warmup", () =>
{
    var sourceFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    var converter = new CaptureSampleConverter(sourceFormat, format);
    var source = Enumerable.Range(0, 960).Select(i => (float)Math.Sin(i * .01)).ToArray();
    var bytes = new byte[source.Length * 4]; Buffer.BlockCopy(source, 0, bytes, 0, bytes.Length);
    var expected = AudioFormatConverter.InterleavedToTargetDuplicatedMono(source, 48000, 2, 48000, 2);
    Check(converter.Convert(bytes, bytes.Length).SequenceEqual(expected), "Capture downmix changed");
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 1000; i++) _ = converter.Convert(bytes, bytes.Length);
    Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Capture conversion allocates every packet");
});
Test("resampler preserves phase across arbitrary packet boundaries", () =>
{
    foreach (var rate in new[] { 16000, 44100, 96000 })
    {
        var inputFormat = new WaveFormat(rate, 16, 1);
        var bytes = new byte[2002];
        for (var i = 0; i < bytes.Length / 2; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 2, 2), (short)(Math.Sin(i * .05) * 8000));
        var whole = new CaptureSampleConverter(inputFormat, format).Convert(bytes, bytes.Length).ToArray();
        var streaming = new CaptureSampleConverter(inputFormat, format); var output = new List<float>();
        for (var offset = 0; offset < bytes.Length;)
        {
            var size = Math.Min((offset % 7 + 1) * 2, bytes.Length - offset);
            output.AddRange(streaming.Convert(bytes.AsSpan(offset, size).ToArray(), size).ToArray()); offset += size;
        }
        Check(output.Count == whole.Length && output.SequenceEqual(whole), "Packet-dependent resampling at " + rate);
    }
});
Test("empty DSP fast path preserves pre-mix gain and limiter", () =>
{
    foreach (var limiter in new[] { false, true })
    {
        var settings = new EffectSettings { EffectOrder = Array.Empty<VoiceEffectKind>(), InputGainDb = 6, LimiterEnabled = limiter, LimiterCeilingDb = -1 };
        var a = Enumerable.Range(0, 1000).Select(i => (i - 500) / 200f).ToArray(); var b = a.ToArray();
        new SimpleVoiceProcessor(settings).ProcessInPlace(a);
        var chain = new VoiceProcessingChain(settings); chain.Process(b);
        Check(chain.IsFastPath && a.SequenceEqual(b), "Fast path changed gain/limiter semantics");
    }
});
Test("returning from Fast to Full clears stateful effects", () =>
{
    var full = new EffectSettings { EffectOrder = new[] { VoiceEffectKind.Echo }, EchoAmount = 1 };
    var chain = new VoiceProcessingChain(full); var impulse = new float[1920]; impulse[0] = .8f; chain.Process(impulse);
    chain.Update(new EffectSettings { EffectOrder = Array.Empty<VoiceEffectKind>() }); Check(chain.IsFastPath, "No Fast path");
    chain.Update(full); Check(!chain.IsFastPath, "Full path not restored");
    var silence = new float[96000]; chain.Process(silence); Check(silence.All(x => x == 0), "Old echo leaked after bypass");
});
Test("capture rejects partial frames and sanitizes non-finite input", () =>
{
    var converter = new CaptureSampleConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), format);
    var bytes = BitConverter.GetBytes(float.NaN); Check(converter.Convert(bytes, 4).ToArray().All(x => x == 0), "NaN reached DSP");
    var rejected = false; try { _ = converter.Convert(bytes, 3); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Partial frame was accepted");
});
Test("stalled output retains only the latest 100ms", () =>
{
    Check(AudioTiming.QueueSamples(44100, 2) == 8820, "Queue capacity loses fractional kHz");
    var queue = new FloatSampleQueue(AudioTiming.QueueSamples(48000, 2));
    var input = Enumerable.Range(0, 96000).Select(i => (float)i).ToArray(); queue.Add(input);
    var output = new float[queue.Count]; queue.Read(output, 0, output.Length);
    Check(output.Length == 9600 && output.SequenceEqual(input.TakeLast(9600)), "Stall replayed old audio");
    Check(queue.DroppedSampleCount == 86400, "Drop diagnostics incorrect");
});
Test("clean capture and render pipeline allocates zero per block", () =>
{
    var converter = new CaptureSampleConverter(format, format);
    var chain = new VoiceProcessingChain(new EffectSettings { EffectOrder = Array.Empty<VoiceEffectKind>() });
    var queue = new FloatSampleQueue(AudioTiming.QueueSamples(48000, 2));
    using var board = new SoundboardTransport(format, false);
    var media = new MediaBridgeTransport(format);
    var mix = new RouteMixSampleProvider(format, AudioRoute.VirtualMicrophone, queue, board, media, new EffectSettings());
    var bytes = new byte[3840]; var output = new float[960];
    void Block()
    {
        var samples = converter.Convert(bytes, bytes.Length); chain.Process(samples);
        queue.Add(samples); mix.Read(output, 0, output.Length);
    }
    Block(); var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 1000; i++) Block();
    Check(GC.GetAllocatedBytesForCurrentThread() == before, "Clean pipeline allocates on every block");
});
Test("equalizer flat and bypass are sample transparent", () =>
{
    var original = Enumerable.Range(0, 1920).Select(i => (float)Math.Sin(i * .1) * .1f).ToArray();
    var samples = original.ToArray(); new MicrophoneEqualizer(48000, 2).Process(samples);
    Check(samples.SequenceEqual(original), "Flat EQ changed samples");
    var settings = new EqualizerSettings { Enabled = false, PreampDb = 12 }; Array.Fill(settings.GainsDb, 12);
    new MicrophoneEqualizer(48000, 2, settings).Process(samples);
    Check(samples.SequenceEqual(original), "Disabled EQ changed samples");
});
Test("equalizer peaking response and channel isolation", () =>
{
    foreach (var gain in new[] { -6.0, 6.0 })
    {
        var settings = new EqualizerSettings(); settings.GainsDb[17] = gain;
        var eq = new MicrophoneEqualizer(48000, 2, settings);
        var samples = new float[96000];
        for (var i = 0; i < 48000; i++) samples[2 * i] = .1f * (float)Math.Sin(2 * Math.PI * 1000 * i / 48000);
        eq.Process(samples);
        double power = 0;
        for (var i = 24000; i < 48000; i++) { power += samples[2 * i] * samples[2 * i]; Check(samples[2 * i + 1] == 0, "Cross-channel leakage"); }
        var measuredDb = 20 * Math.Log10(Math.Sqrt(power / 24000) / (.1 / Math.Sqrt(2)));
        Check(Math.Abs(measuredDb - gain) < .05, "Wrong peak gain: " + measuredDb);
    }
});
Test("equalizer global profile roundtrip and defensive normalization", () =>
{
    var settings = new EqualizerSettings { PreampDb = -3.5 }; settings.GainsDb[4] = 2.5;
    var restored = JsonSerializer.Deserialize<EqualizerSettings>(JsonSerializer.Serialize(settings))!.NormalizedCopy();
    Check(restored.PreampDb == -3.5 && restored.GainsDb.SequenceEqual(settings.GainsDb), "Curve lost on persistence");
    settings.GainsDb = new[] { double.NaN, 200.0, -200.0 }; settings.PreampDb = double.PositiveInfinity;
    var safe = settings.NormalizedCopy();
    Check(safe.GainsDb.Length == 31 && safe.GainsDb[0] == 0 && safe.GainsDb[1] == 12 && safe.GainsDb[2] == -12 && safe.PreampDb == 0, "Malformed curve not sanitized");
    settings.GainsDb = null!; Check(settings.NormalizedCopy().GainsDb.All(x => x == 0), "Null curve not migrated");
});
Test("coarse equalizer edits preserve fine curve and adjacent anchors", () =>
{
    var settings = new EqualizerSettings(); settings.GainsDb[16] = 1.25;
    var original = settings.GainsDb.ToArray();
    settings.SetBasicGain(5, 3);
    Check(settings.GainsDb[17] == 3 && settings.GainsDb[14] == 0 && settings.GainsDb[20] == 0, "Basic band changed adjacent anchors");
    Check(settings.GainsDb[16] > 1.25 && settings.GainsDb[15] > 0 && settings.GainsDb[18] > 0, "Coarse edit did not cover nearby frequencies");
    settings.SetBasicGain(5, 0);
    Check(settings.GainsDb.Zip(original).All(p => Math.Abs(p.First - p.Second) < 1e-12), "Fine detail was discarded");
    settings.SetBasicGain(0, 4); settings.SetBasicGain(9, -4);
    Check(settings.GainsDb[0] == 4 && settings.GainsDb[30] == -4, "Outer bands are unreachable in basic mode");
});
Test("equalizer transitions are bounded and reach new preamp", () =>
{
    var eq = new MicrophoneEqualizer(48000, 2);
    eq.Update(new EqualizerSettings { PreampDb = -6 });
    var samples = Enumerable.Repeat(.1f, 2880).ToArray(); eq.Process(samples);
    Check(samples[0] == .1f && samples.All(float.IsFinite), "Transition is not continuous at start");
    for (var i = 2; i < samples.Length; i += 2) Check(Math.Abs(samples[i] - samples[i - 2]) < .0001, "Preamp transition clicked");
    Check(Math.Abs(samples[^1] - .1 * Math.Pow(10, -6.0 / 20)) < 1e-7, "Transition did not settle");
});
Test("31-band EQ is stable and allocation-free under updates", () =>
{
    var settings = new EqualizerSettings { PreampDb = -12 };
    for (var i = 0; i < 31; i++) settings.GainsDb[i] = i % 2 == 0 ? 12 : -12;
    var eq = new MicrophoneEqualizer(48000, 2, settings); var samples = new float[960];
    for (var i = 0; i < samples.Length; i++) samples[i] = .01f * (float)Math.Sin(i * .3);
    eq.Process(samples); var before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.GetTimestamp();
    for (var i = 0; i < 1000; i++) eq.Process(samples);
    var elapsed = Stopwatch.GetElapsedTime(watch);
    Check(GC.GetAllocatedBytesForCurrentThread() == before, "EQ allocates per audio block");
    Check(samples.All(float.IsFinite), "EQ became unstable");
    Console.WriteLine($"EQ_31_BANDS_10S_AUDIO_MS={elapsed.TotalMilliseconds:F1}; EQ_BYTES_PER_BLOCK=0");
    settings.Enabled = false; eq.Update(settings); Array.Fill(samples, .1f); eq.Process(samples); Array.Fill(samples, .1f); eq.Process(samples);
    Array.Fill(samples, .1f); eq.Process(samples); Check(samples.All(x => x == .1f), "Bypass did not settle");
});
Test("quick tone controls preserve detailed offsets away from clipping", () =>
{
    var settings = new EqualizerSettings(); settings.GainsDb[12] = .75;
    var original = settings.GainsDb.ToArray();
    for (var i = 0; i < 3; i++) settings.SetToneGain(i, 2);
    Check(settings.GainsDb[8] > 0 && settings.GainsDb[20] > 0 && settings.GainsDb[26] > 0, "Tone controls do not affect their frequency ranges");
    for (var i = 0; i < 3; i++) settings.SetToneGain(i, 0);
    Check(settings.GainsDb.Zip(original).All(x => Math.Abs(x.First - x.Second) < 1e-12), "Tone adjustments discarded fine offsets");
});
Test("voice starting presets are independent and survive save", () =>
{
    for (var i = 0; i < 4; i++)
    {
        var settings = EqualizerSettings.FromPreset(i);
        var restored = JsonSerializer.Deserialize<EqualizerSettings>(JsonSerializer.Serialize(settings))!.NormalizedCopy();
        Check(settings.GainsDb.SequenceEqual(restored.GainsDb) && settings.ToneGainsDb.SequenceEqual(restored.ToneGainsDb), "Preset lost control positions");
        Check(settings.GainsDb.All(x => x >= -12 && x <= 12) && settings.PreampDb <= 0, "Unsafe preset gain");
        settings.GainsDb[0] = 12;
        Check(EqualizerSettings.FromPreset(i).GainsDb[0] != 12, "Presets share mutable arrays");
    }
    var legacy = JsonSerializer.Deserialize<EqualizerSettings>("{\"GainsDb\":[1],\"ToneGainsDb\":null}")!.NormalizedCopy();
    Check(legacy.GainsDb[0] == 1 && legacy.ToneGainsDb.All(x => x == 0), "Old user profile migration lost EQ");
});
Test("parametric EQ response matches processed audio for every filter type", () =>
{
    foreach (var type in Enum.GetValues<EqualizerFilter>())
    {
        var settings = new EqualizerSettings();
        settings.ParametricBands[2] = new EqualizerBand { Frequency = 1000, GainDb = 6, Q = .707, Filter = type };
        var eq = new MicrophoneEqualizer(48000, 2, settings); var samples = new float[96000];
        for (var i = 0; i < 48000; i++) samples[2 * i] = .01f * (float)Math.Sin(2 * Math.PI * 1000 * i / 48000);
        eq.Process(samples); double power = 0;
        for (var i = 24000; i < 48000; i++) power += samples[2 * i] * samples[2 * i];
        var db = 20 * Math.Log10(Math.Sqrt(power / 24000) / (.01 / Math.Sqrt(2)));
        Check(Math.Abs(db - settings.GetResponseFilters().Sum(f => f.ResponseDb(1000))) < .05, "Graph disagrees with DSP: " + type);
    }
    var wide = EqualizerCoefficients.Create(1000, 6, .5, 48000);
    var narrow = EqualizerCoefficients.Create(1000, 6, 10, 48000);
    Check(wide.ResponseDb(2000) > narrow.ResponseDb(2000) + 1, "Q does not change bandwidth");
    Check(Math.Abs(narrow.ResponseDb(1000) - 6) < 1e-6, "Q changed peak gain");
});
Test("parametric updates change audio and persist independently of graphic bands", () =>
{
    var settings = new EqualizerSettings(); settings.GainsDb[0] = 1;
    var eq = new MicrophoneEqualizer(48000, 2, settings);
    settings.ParametricBands[2].GainDb = -6; settings.ParametricBands[2].Frequency = 1080; settings.ParametricBands[2].Q = 1.182;
    eq.Update(settings); var signal = new float[96000];
    for (var i = 0; i < 48000; i++) signal[i * 2] = .1f * (float)Math.Sin(2 * Math.PI * 1080 * i / 48000);
    eq.Process(signal);
    Check(signal.TakeLast(48000).Max() < .055, "Parametric change was ignored");
    var restored = JsonSerializer.Deserialize<EqualizerSettings>(JsonSerializer.Serialize(settings))!.NormalizedCopy();
    Check(restored.ParametricBands.SequenceEqual(settings.ParametricBands) && restored.GainsDb[0] == 1, "Persistence lost either curve");
    restored.ParametricBands[2].Enabled = false; eq.Update(restored);
    Array.Fill(signal, .1f); eq.Process(signal); Check(Math.Abs(signal[^1] - .1f) < .0001, "Disabled band still affects audio");
    Check(settings.ParametricBands[2].Enabled, "Settings copy shares band objects");
});
Test("parametric malformed profiles migrate safely and extreme filters remain stable", () =>
{
    var settings = new EqualizerSettings { ParametricBands = new[] { new EqualizerBand { Frequency = double.NaN, GainDb = double.PositiveInfinity, Q = 0, Filter = (EqualizerFilter)99 }, null! } }.NormalizedCopy();
    Check(settings.ParametricBands.Length == 2 && settings.ParametricBands[0].Frequency == 1000 && settings.ParametricBands[0].Q == .2 && settings.ParametricBands[0].Filter == EqualizerFilter.Peak, "Invalid band not sanitized");
    foreach (var type in Enum.GetValues<EqualizerFilter>())
    foreach (var frequency in new[] { 20.0, 20000 })
    {
        settings = new EqualizerSettings(); settings.ParametricBands[0] = new EqualizerBand { Frequency = frequency, GainDb = 12, Q = 12, Filter = type };
        var eq = new MicrophoneEqualizer(48000, 2, settings); var samples = new float[96000]; samples[0] = .01f; eq.Process(samples);
        Check(samples.All(float.IsFinite) && samples.Max(Math.Abs) < 1, "Extreme filter became unstable: " + type);
    }
    settings.ParametricBands = null!; Check(settings.NormalizedCopy().ParametricBands.All(b => b.GainDb == 0), "Null parametric settings not migrated");
});
Test("combined graphic and parametric processing allocates zero per block", () =>
{
    var settings = new EqualizerSettings(); Array.Fill(settings.GainsDb, -1);
    for (var i = 0; i < 5; i++) settings.ParametricBands[i].GainDb = 2;
    var eq = new MicrophoneEqualizer(48000, 2, settings); var samples = new float[960]; eq.Process(samples);
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 1000; i++) eq.Process(samples);
    Check(GC.GetAllocatedBytesForCurrentThread() == before, "Combined EQ allocates per block");
});
Test("custom EQ points preserve count, values and independent copies", () =>
{
    var settings = new EqualizerSettings(); settings.AddPoint(2400, -3.5);
    var restored = JsonSerializer.Deserialize<EqualizerSettings>(JsonSerializer.Serialize(settings))!.NormalizedCopy();
    Check(restored.ParametricBands.Length == 6 && restored.ParametricBands[^1].Frequency == 2400 && restored.ParametricBands[^1].GainDb == -3.5, "Custom point lost during load");
    restored.ParametricBands[^1].GainDb = 4; Check(settings.ParametricBands[^1].GainDb == -3.5, "Points share mutable references");
    while(settings.AddPoint()) { }
    Check(settings.ParametricBands.Length == 16 && !settings.AddPoint(), "Point limit is unbounded");
    while(settings.RemovePoint(0)) { }
    Check(settings.ParametricBands.Length == 1 && !settings.RemovePoint(0), "Last point removed");
    Check(settings.NormalizedCopy().ParametricBands.Length == 1, "Deleted points returned after load");
});
Test("width handles change bandwidth symmetrically without changing center or gain", () =>
{
    var band = new EqualizerBand { Frequency = 1000, GainDb = -5.8, Q = 1.182 };
    var original = band.Q; var right = band.Frequency * band.WidthRatio;
    band.SetWidthFromEdge(right); Check(Math.Abs(band.Q - original) < 1e-10, "Width handle does not roundtrip");
    band.SetWidthFromEdge(2000); var wideQ = band.Q; band.SetWidthFromEdge(500);
    Check(Math.Abs(band.Q - wideQ) < 1e-10 && wideQ < original, "Left and right handles disagree");
    band.SetWidthFromEdge(1010); Check(band.Q > original && band.Q <= 12, "Narrowing is inverted or unbounded");
    Check(band.Frequency == 1000 && band.GainDb == -5.8, "Width edit changed pitch or gain");
});
Test("WebRTC cancels a delayed loudspeaker reference on CPU", () =>
{
    using var echo = new WebRtcEchoCanceller();
    var random = new Random(187); var far = new float[48000 * 8];
    for (var i = 0; i < far.Length; i++) far[i] = (float)(random.NextDouble() - .5) * .2f;
    var render = new float[960]; var mic = new float[960]; double before = 0, after = 0;
    for (var offset = 0; offset < far.Length; offset += 480)
    {
        for (var i = 0; i < 480; i++)
        {
            render[i * 2] = render[i * 2 + 1] = far[offset + i];
            var value = offset + i >= 960 ? .6f * far[offset + i - 960] : 0;
            mic[i * 2] = mic[i * 2 + 1] = value;
            if (offset > 48000 * 4) before += value * value;
        }
        echo.AddReference(render); echo.Process(mic);
        if (offset > 48000 * 4) for (var i = 0; i < 480; i++) after += mic[i * 2] * mic[i * 2];
    }
    var erle = 10 * Math.Log10(before / Math.Max(after, 1e-20)); Console.WriteLine($"AEC_SYNTHETIC_ERLE_DB={erle:F1}");
    Check(erle > 12, "Delayed playback was not cancelled");
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 100; i++) { echo.AddReference(render); echo.Process(mic); }
    Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "AEC allocates per block");
});
Test("room echo reducer attenuates repeated room reflections without allocating", () =>
{
    var room = new RoomEchoReducer(); var random = new Random(189);
    const int length = 48000 * 12, period = 24000, burst = 4800, delay = 1023;
    var reverberant = new float[length]; var output = new float[length]; var block = new float[960];
    for (var i = 0; i < length; i++)
    {
        var dry = i % period < burst ? (float)(random.NextDouble() - .5) * .2f : 0;
        reverberant[i] = dry + (i >= 1920 ? .7f * reverberant[i - 1920] : 0);
    }
    var watch = Stopwatch.StartNew();
    for (var offset = 0; offset < length; offset += 480)
    {
        for (var i = 0; i < 480; i++) block[i * 2] = block[i * 2 + 1] = reverberant[offset + i];
        room.Process(block); for (var i = 0; i < 480; i++) output[offset + i] = block[i * 2];
    }
    watch.Stop(); double before = 0, after = 0, directBefore = 0, directAfter = 0;
    for (var i = 48000 * 6; i < length - delay; i++)
    {
        var phase = i % period;
        if (phase >= burst + 3000 && phase < 18000) { before += reverberant[i] * reverberant[i]; after += output[i + delay] * output[i + delay]; }
        if (phase < 1200) { directBefore += reverberant[i] * reverberant[i]; directAfter += output[i + delay] * output[i + delay]; }
    }
    var attenuation = 10 * Math.Log10(before / Math.Max(after, 1e-20));
    Console.WriteLine($"WPE_SYNTHETIC_TAIL_DB={attenuation:F1}; WPE_12S_AUDIO_MS={watch.Elapsed.TotalMilliseconds:F0}; DIRECT_RATIO={directAfter/directBefore:F2}");
    Check(attenuation > 2, "Room tail not reduced");
    Check(directAfter / directBefore > .6 && output.All(float.IsFinite), "Direct speech damaged or unstable output");
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 100; i++) room.Process(block);
    Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "WPE allocates per block");
});
Test("room processing preserves clean voiced speech and arbitrary packet boundaries", () =>
{
    var whole = new RoomEchoReducer(); var split = new RoomEchoReducer();
    var signal = new float[48000 * 2 * 3];
    for (var i = 0; i < signal.Length / 2; i++) signal[i * 2] = signal[i * 2 + 1] = .08f * (float)(Math.Sin(i * 2 * Math.PI * 137 / 48000) + .3 * Math.Sin(i * 2 * Math.PI * 274 / 48000));
    var a = signal.ToArray(); var b = signal.ToArray(); whole.Process(a);
    for (var offset = 0; offset < b.Length; ) { var count = Math.Min(274, b.Length - offset); split.Process(b.AsSpan(offset, count)); offset += count; }
    Check(a.SequenceEqual(b), "WPE depends on WASAPI packet boundaries");
    double before = 0, after = 0;
    for (var i = 48000; i < signal.Length - 2046; i++) { before += signal[i] * signal[i]; after += a[i + 2046] * a[i + 2046]; }
    Check(after / before > .7 && after / before < 1.3, "Clean voiced speech was strongly attenuated");
});
Test("AEC preserves near-end speech when the reference is silent", () =>
{
    using var echo = new WebRtcEchoCanceller(); var render = new float[960]; var mic = new float[960];
    double before = 0, after = 0;
    for (var block = 0; block < 300; block++)
    {
        for (var i = 0; i < 480; i++) mic[i * 2] = mic[i * 2 + 1] = .1f * (float)Math.Sin((block * 480 + i) * 2 * Math.PI * 193 / 48000);
        if (block > 100) foreach(var sample in mic) before += sample * sample;
        echo.AddReference(render); echo.Process(mic);
        if (block > 100) foreach(var sample in mic) after += sample * sample;
    }
    Check(after / before > .8 && after / before < 1.2, "AEC damaged near-end speech without loudspeaker sound");
    echo.Dispose(); var rejected = false; try { echo.Process(mic); } catch(ObjectDisposedException) { rejected = true; }
    Check(rejected, "Disposed AEC accepted audio");
});
Console.WriteLine($"REGRESSION_TESTS=PASS ({results.Count})");
// Every directory here was created by this process under a fixed temporary root.
var expectedRoot = Path.GetFullPath("artifacts/regression-tests") + Path.DirectorySeparatorChar;
if (!root.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test cleanup path.");
Directory.Delete(root, recursive: true);
