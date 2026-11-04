using NAudio.Wave;
using System.Diagnostics;
using System.Text.Json;
using VoiSe.Audio;

double Option(string name, double fallback)
{
    var index = Array.IndexOf(args, name);
    return index < 0 ? fallback : double.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture);
}
var seconds = Math.Clamp(Option("--seconds", 5), 1, 120);
var soakMinutes = Math.Clamp(Option("--soak-minutes", 0), 0, 480);
var runRoot = Path.GetFullPath(Path.Combine("artifacts", "performance", "14.9.1", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
Directory.CreateDirectory(runRoot);
var wavePath = Path.Combine(runRoot, "synthetic.wav");
using (var writer = new WaveFileWriter(wavePath, new WaveFormat(48000, 16, 1)))
{
    var signal = Enumerable.Range(0, 48000).Select(i => .04f * (float)Math.Sin(i * 2 * Math.PI * 173 / 48000)).ToArray();
    for (var i = 0; i < 4; i++) writer.WriteSamples(signal, 0, signal.Length);
}
var reports = new List<object>();
foreach (var scenario in new[] { "clean", "rnnoise", "deepfilter", "equalizer31", "effects", "soundboard8", "media", "echo" })
{
    using var pipeline = new Pipeline(scenario, wavePath);
    for (var i = 0; i < 100; i++) { pipeline.Process(i); if (scenario == "deepfilter") Thread.Sleep(10); }
    var timings = new List<double>(); var allocations = 0L;
    using var process = Process.GetCurrentProcess();
    var startCpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
    var blocks = 0;
    while (watch.Elapsed.TotalSeconds < seconds)
    {
        var before = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        pipeline.Process(blocks++);
        timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        allocations += GC.GetAllocatedBytesForCurrentThread() - before;
        var remainingMs = blocks * 10 - watch.Elapsed.TotalMilliseconds;
        if (remainingMs >= 1) Thread.Sleep((int)remainingMs);
    }
    process.Refresh(); timings.Sort();
    double Percentile(double p) => timings[Math.Min(timings.Count - 1, (int)Math.Ceiling(timings.Count * p) - 1)];
    reports.Add(new { Scenario = scenario, Blocks = blocks, AudioSeconds = blocks * .01, ElapsedSeconds = watch.Elapsed.TotalSeconds,
        ProcessingP50Ms = Percentile(.50), ProcessingP95Ms = Percentile(.95), ProcessingP99Ms = Percentile(.99), ProcessingMaxMs = timings[^1],
        CpuOneCorePercent = (process.TotalProcessorTime - startCpu).TotalSeconds / watch.Elapsed.TotalSeconds * 100,
        CpuMachinePercent = (process.TotalProcessorTime - startCpu).TotalSeconds / watch.Elapsed.TotalSeconds * 100 / Environment.ProcessorCount,
        AllocatedBytesPerBlock = allocations / (double)blocks, WorkingSetMiB = process.WorkingSet64 / 1048576d,
        PrivateMiB = process.PrivateMemorySize64 / 1048576d, CachedPcmBytes = SoundFileLoader.CachedBytes });
    Console.WriteLine("PROFILE=" + scenario + "; blocks=" + blocks + "; p99_ms=" + Percentile(.99).ToString("F3"));
}
object? soak = null;
if (soakMinutes > 0)
{
    using var pipeline = new Pipeline("soak", wavePath);
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
    var samples = new List<object>(); var watch = Stopwatch.StartNew();
    var nextCheckpoint = TimeSpan.Zero; var blocks = 0;
    Console.WriteLine("SOAK_STARTED; minutes=" + soakMinutes);
    while (watch.Elapsed.TotalMinutes < soakMinutes)
    {
        pipeline.Process(blocks++);
        if (blocks % 2000 == 0) pipeline.Reconfigure();
        if (watch.Elapsed >= nextCheckpoint)
        {
            using var process = Process.GetCurrentProcess(); process.Refresh();
            samples.Add(new { Seconds = watch.Elapsed.TotalSeconds, PrivateBytes = process.PrivateMemorySize64,
                WorkingSet = process.WorkingSet64, ManagedBytes = GC.GetTotalMemory(false), Handles = process.HandleCount,
                Threads = process.Threads.Count, CachedPcmBytes = SoundFileLoader.CachedBytes });
            nextCheckpoint = watch.Elapsed + TimeSpan.FromSeconds(10);
        }
        if (SoundFileLoader.CachedBytes > SoundFileLoader.CacheBudgetBytes) throw new Exception("PCM cache exceeded its budget.");
        if (blocks % 100 == 0) await Task.Delay(1);
    }
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var retainedAfter = GC.GetTotalMemory(forceFullCollection: true);
    if (retainedAfter - retainedBefore > 8 * 1024 * 1024) throw new Exception("Retained managed growth exceeded 8 MiB in a bounded, repeating workload.");
    soak = new { Status = "PASS", ElapsedMinutes = watch.Elapsed.TotalMinutes, Blocks = blocks, SyntheticAudioHours = blocks * .01 / 3600,
        RetainedBeforeBytes = retainedBefore, RetainedAfterBytes = retainedAfter, RetainedGrowthBytes = retainedAfter - retainedBefore, Samples = samples };
    Console.WriteLine("SOAK_FINISHED; blocks=" + blocks);
}
var output = new { Scope = "Synthetic component workload without physical endpoints, WinUI, network or a communication application. Scenarios are paced to 10 ms blocks. The separate soak is accelerated and exercises more audio than its wall time. These figures are not whole-app RAM/CPU or end-to-end latency. Per-block allocation totals include timing-list growth and are upper bounds.",
    Runtime = Environment.Version.ToString(), LogicalProcessors = Environment.ProcessorCount, Scenarios = reports, Soak = soak };
File.WriteAllText(Path.Combine(runRoot, "report.json"), JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PERFORMANCE_REPORT=" + Path.Combine(runRoot, "report.json"));

sealed class Pipeline : IDisposable
{
    private readonly string _scenario, _wave;
    private readonly float[] _microphone = new float[960], _render = new float[960], _output = new float[960], _monitor = new float[960];
    private readonly FloatSampleQueue _queue = new(9600);
    private readonly SoundboardTransport _sounds = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
    private readonly MediaBridgeTransport _media = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
    private readonly RouteMixSampleProvider _virtual, _headphones;
    private readonly VoiceProcessingChain _voice;
    private readonly MicrophoneEqualizer _equalizer;
    private readonly RnNoiseSuppressionProcessor _rn;
    private readonly DeepFilterNetSuppressionProcessor _df;
    private readonly WebRtcEchoCanceller? _echo;
    private readonly RoomEchoReducer? _room;
    private double _phase;
    private bool _changed;
    public Pipeline(string scenario, string wave)
    {
        _scenario = scenario; _wave = wave;
        var settings = new EffectSettings { EffectOrder = scenario is "effects" or "soak" ? new[] { VoiceEffectKind.Pitch, VoiceEffectKind.Robot, VoiceEffectKind.Echo } : Array.Empty<VoiceEffectKind>(),
            PitchSemitones = 3, RobotAmount = .2f, EchoAmount = .1f, GateEnabled = false, CompressorEnabled = false };
        _voice = new(settings);
        var eq = new EqualizerSettings();
        if (scenario is "equalizer31" or "soak") for (var i = 0; i < 31; i++) eq.GainsDb[i] = i % 2 == 0 ? 2 : -2;
        _equalizer = new(48000, 2, eq);
        _rn = new(scenario is "rnnoise" or "soak", .7f); _df = new(scenario == "deepfilter", .7f);
        if (scenario == "rnnoise" && !_rn.IsAvailable) throw new Exception(_rn.InitializationError);
        if (scenario == "deepfilter" && !_df.IsAvailable) throw new Exception(_df.InitializationError);
        if (scenario == "echo") { _echo = new(); _room = new(); }
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        _virtual = new(format, AudioRoute.VirtualMicrophone, _queue, _sounds, _media, settings);
        _headphones = new(format, AudioRoute.Monitor, new FloatSampleQueue(9600), _sounds, _media, settings);
        if (scenario is "soundboard8" or "soak") Reconfigure();
    }
    public void Reconfigure()
    {
        _sounds.Stop();
        for (var i = 0; i < 8; i++) _sounds.Play(_wave, .05f, .05f, 0, playbackKey: "voice-" + i);
        var settings = new EqualizerSettings(); _changed = !_changed;
        for (var i = 0; i < 31; i++) settings.GainsDb[i] = _changed ? 2 : -2;
        _equalizer.Update(settings);
    }
    public void Process(int block)
    {
        for (var i = 0; i < 480; i++)
        {
            var value = .04f * (float)Math.Sin(_phase); _phase += 2 * Math.PI * 173 / 48000;
            _microphone[i * 2] = _microphone[i * 2 + 1] = value;
            _render[i * 2] = _render[i * 2 + 1] = .01f * (float)Math.Sin(_phase * 2);
        }
        _echo?.AddReference(_render); _echo?.Process(_microphone); _room?.Process(_microphone);
        _rn.ProcessInPlace(_microphone); _df.ProcessInPlace(_microphone);
        _equalizer.Process(_microphone); _voice.Process(_microphone); _queue.Add(_microphone);
        if (_scenario is "media" or "soak") _media.AddSamples(_render);
        _virtual.Read(_output, 0, _output.Length); _headphones.Read(_monitor, 0, _monitor.Length);
        foreach (var value in _output) if (!float.IsFinite(value) || Math.Abs(value) > 1.01f) throw new Exception("Invalid audio output.");
    }
    public void Dispose() { _sounds.Dispose(); _rn.Dispose(); _df.Dispose(); _echo?.Dispose(); }
}
