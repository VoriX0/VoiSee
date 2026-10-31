using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;
using VoiSe.Audio;

// Hardware-independent audit of unmodified audio sources at b1829bb.
// No audio devices, user settings, native denoisers, or UI are opened.
var results = new List<object>();
var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
const int count = 1920; // 20 ms stereo
var block = new float[count];
for (var i = 0; i < block.Length; i++) block[i] = (float)(0.1 * Math.Sin(i / 2 * 2 * Math.PI * 180 / 48000));
var original = block.ToArray();
void Measure(string name, Action action, int iterations, double audioMs = 20)
{
    for (var i = 0; i < 100; i++) action();
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
    var watch = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++) action();
    watch.Stop();
    var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalMilliseconds;
    var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    var row = new { name, iterations, wallMs = watch.Elapsed.TotalMilliseconds, cpuMs,
        msPerBlock = watch.Elapsed.TotalMilliseconds / iterations,
        oneCoreRealtimePercent = cpuMs / (iterations * audioMs) * 100,
        bytesPerBlock = bytes / (double)iterations,
        allocationMiBPerAudioSecond = bytes / (double)iterations * (1000 / audioMs) / 1048576 };
    results.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
}
if (args.Length > 0 && args[0] == "native")
{
    var mode = args[1];
    using var self = Process.GetCurrentProcess();
    GC.Collect(); self.Refresh();
    var beforePrivate = self.PrivateMemorySize64;
    var beforeWorking = self.WorkingSet64;
    IDisposable processor;
    Action process;
    string? error;
    if (mode == "RNNoise") {
        var rn = new RnNoiseSuppressionProcessor(true, .7f);
        processor = rn; process = () => rn.ProcessInPlace(block); error = rn.InitializationError;
    } else {
        var df = new DeepFilterNetSuppressionProcessor(true, .7f);
        processor = df; process = () => df.ProcessInPlace(block); error = df.InitializationError;
    }
    if (error is not null) throw new InvalidOperationException(error);
    using (processor) {
        for (var i = 0; i < 100; i++) { original.CopyTo(block, 0); process(); Thread.Sleep(20); }
        var cpuStart = self.TotalProcessorTime;
        var duration = Stopwatch.StartNew();
        double maxBlock = 0;
        for (var i = 0; i < 250; i++) {
            original.CopyTo(block, 0);
            var started = Stopwatch.GetTimestamp(); process();
            maxBlock = Math.Max(maxBlock, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            var wait = (i + 1) * 20 - duration.Elapsed.TotalMilliseconds;
            if (wait > 0) Thread.Sleep((int)wait);
        }
        duration.Stop();
        var cpuMs = (self.TotalProcessorTime - cpuStart).TotalMilliseconds;
        GC.Collect(); self.Refresh();
        var row = new { mode, wallMs = duration.Elapsed.TotalMilliseconds, cpuMs,
            oneCorePercent = cpuMs / duration.Elapsed.TotalMilliseconds * 100,
            maxBlockMs = maxBlock, privateMiB = self.PrivateMemorySize64 / 1048576.0,
            workingSetMiB = self.WorkingSet64 / 1048576.0,
            privateDeltaMiB = (self.PrivateMemorySize64 - beforePrivate) / 1048576.0,
            workingSetDeltaMiB = (self.WorkingSet64 - beforeWorking) / 1048576.0,
            note = "Synthetic paced 48k stereo audio; dedicated console process, no UI or audio devices" };
        var json = JsonSerializer.Serialize(row, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "native-" + mode + ".json"), json);
    }
    return;
}
var ctorStart = GC.GetAllocatedBytesForCurrentThread();
var empty = new SimpleVoiceProcessor(new EffectSettings { EffectOrder = Array.Empty<VoiceEffectKind>() });
results.Add(new { name = "DSP constructor", managedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - ctorStart });
Measure("DSP empty chain", () => { original.CopyTo(block, 0); empty.ProcessInPlace(block); }, 2000);
foreach (var kind in Enum.GetValues<VoiceEffectKind>())
{
    var settings = new EffectSettings {
        EffectOrder = new[] { kind }, PitchSemitones = 5, FormantShiftSemitones = 3,
        BassAmount = .5f, TrebleAmount = .5f, DistortionAmount = .5f, RobotAmount = .5f,
        TremoloAmount = .5f, EchoAmount = .5f, ReverbAmount = .5f, RadioAmount = .5f,
        BitCrusherAmount = .5f, AlienAmount = .5f, ChorusAmount = .5f, FlangerAmount = .5f,
        PhaserAmount = .5f, VibratoAmount = .5f, DoublerAmount = .5f, RingModAmount = .5f,
        ChipmunkAmount = .5f, GiantAmount = .5f, GhostAmount = .5f, StutterAmount = .5f,
        WobblyAmount = .5f, GenderMaleAmount = .5f, GenderFemaleAmount = .5f,
        PossessedAmount = .5f, MegaphoneAmount = .5f, HelicopterAmount = .5f,
        CyborgAmount = .5f, BrokenRadioAmount = .5f, VocoderAmount = .5f };
    var processor = new SimpleVoiceProcessor(settings);
    Measure("DSP " + kind, () => { original.CopyTo(block, 0); processor.ProcessInPlace(block); }, 400);
}
Measure("Capture conversion 48k stereo", () => AudioFormatConverter.InterleavedToTargetDuplicatedMono(original, 48000, 2, 48000, 2), 4000);
var queue = new FloatSampleQueue(192000);
Measure("Queue add/read two routes", () => { queue.Add(original); queue.Read(block, 0, count); queue.Add(original); queue.Read(block, 0, count); }, 4000);
var dataRoot = Path.Combine(AppContext.BaseDirectory, "synthetic");
Directory.CreateDirectory(dataRoot);
string MakeWave(int seconds, string name)
{
    var path = Path.Combine(dataRoot, name + ".wav");
    using var writer = new WaveFileWriter(path, new WaveFormat(48000, 16, 1));
    var bytes = new byte[96000];
    for (var i = 0; i < 48000; i++) {
        short sample = (short)(Math.Sin(i * 2 * Math.PI * 180 / 48000) * 3000);
        bytes[i * 2] = (byte)sample; bytes[i * 2 + 1] = (byte)(sample >> 8);
    }
    for (var i = 0; i < seconds; i++) writer.Write(bytes, 0, bytes.Length);
    return path;
}
var shortWave = MakeWave(60, "short");
foreach (var voices in new[] { 0, 1, 8, 32 })
{
    var transport = new SoundboardTransport(format);
    for (var i = 0; i < voices; i++) transport.Play(shortWave, 1, 1, 0, false, "voice:" + i);
    Measure("Soundboard two routes, voices=" + voices, () => {
        transport.Read(AudioRoute.VirtualMicrophone, block, 0, count);
        transport.Read(AudioRoute.Monitor, block, 0, count);
    }, 2000);
}
var oneRoute = new SoundboardTransport(format);
oneRoute.Play(shortWave, 1, 0, 0, false, "one-route");
for (var i = 0; i < 3100; i++) oneRoute.Read(AudioRoute.VirtualMicrophone, block, 0, count);
results.Add(new { name = "One-route completion after 62s of 60s sound", stillActive = oneRoute.IsActive,
    status = oneRoute.GetStatus("one-route") });
SoundFileLoader.ClearCache();
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
var beforeCache = GC.GetTotalMemory(true);
var cacheAllocStart = GC.GetTotalAllocatedBytes(true);
var decodeWatch = Stopwatch.StartNew();
for (var i = 0; i < 3; i++) {
    var path = MakeWave(60, "minute-" + i);
    _ = SoundFileLoader.LoadToFormat(path, format);
}
decodeWatch.Stop();
var cached = GC.GetTotalMemory(true);
results.Add(new { name = "Cache 3 x 60s mono PCM16 -> 48k stereo float",
    retainedMiB = (cached - beforeCache) / 1048576.0,
    totalAllocatedMiB = (GC.GetTotalAllocatedBytes(true) - cacheAllocStart) / 1048576.0,
    decodeWallMs = decodeWatch.Elapsed.TotalMilliseconds });
SoundFileLoader.ClearCache();
var afterClear = GC.GetTotalMemory(true);
results.Add(new { name = "Cache cleared", retainedMiB = (afterClear - beforeCache) / 1048576.0 });
var store = new VoiSe.App.SoundBoardLibraryStore(Path.Combine(dataRoot, Guid.NewGuid().ToString("N")));
var library = store.Load();
var stored = store.AddSound(library, shortWave, library.Categories[0]);
var oldPath = stored.FilePath;
var replacement = MakeWave(1, "locked-replacement");
string? replacementError = null;
using (var held = new FileStream(replacement, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
    try { store.ReplaceSoundFile(library, stored, replacement); }
    catch (Exception ex) { replacementError = ex.GetType().Name; }
}
results.Add(new { name = "ReplaceSoundFile with locked source", error = replacementError,
    oldFileSurvived = File.Exists(oldPath), libraryStillReferencesOldFile = stored.FilePath == oldPath });
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "results.json"), JsonSerializer.Serialize(new {
    baseline = "b1829bb", framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    processorCount = Environment.ProcessorCount, os = Environment.OSVersion.ToString(), results
}, new JsonSerializerOptions { WriteIndented = true }));
