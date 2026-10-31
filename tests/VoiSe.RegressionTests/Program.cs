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
Console.WriteLine($"REGRESSION_TESTS=PASS ({results.Count})");
// Every directory here was created by this process under a fixed temporary root.
var expectedRoot = Path.GetFullPath("artifacts/regression-tests") + Path.DirectorySeparatorChar;
if (!root.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test cleanup path.");
Directory.Delete(root, recursive: true);
