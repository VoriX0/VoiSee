using VoiSe.Audio;

var options = CommandLineOptions.Parse(args);

if (options.ShowHelp)
{
    PrintHelp();
    return 0;
}

using var catalog = new AudioDeviceCatalog();

if (options.ListDevices)
{
    PrintDevices(catalog);
    return 0;
}

var input = catalog.FindCaptureDevice(options.InputQuery);
if (input is null)
{
    Console.Error.WriteLine("Input microphone was not found. Use --list-devices to inspect available devices.");
    return 2;
}

var virtualOutput = catalog.FindRenderDevice(options.VirtualOutputQuery ?? "CABLE Input");
if (virtualOutput is null)
{
    Console.Error.WriteLine("Virtual output was not found. Install VB-CABLE and use --list-devices. Expected render device usually contains: CABLE Input");
    return 3;
}

var monitor = string.IsNullOrWhiteSpace(options.MonitorQuery)
    ? null
    : catalog.FindRenderDevice(options.MonitorQuery);

if (!string.IsNullOrWhiteSpace(options.MonitorQuery) && monitor is null)
{
    Console.Error.WriteLine("Monitor output was not found. Use --list-devices to inspect available render devices.");
    return 4;
}

var settings = new EffectSettings
{
    InputGainDb = options.InputGainDb,
    VoiceGainDb = options.VoiceGainDb,
    GateEnabled = !options.DisableGate,
    GateThresholdDb = options.GateThresholdDb,
    CompressorEnabled = !options.DisableCompressor,
    CompressorThresholdDb = options.CompressorThresholdDb,
    CompressorRatio = options.CompressorRatio,
    LimiterEnabled = !options.DisableLimiter,
    LimiterCeilingDb = options.LimiterCeilingDb
};

Console.WriteLine("VoiSe Gate 0 Audio Prototype");
Console.WriteLine($"Input:          {input.FriendlyName}");
Console.WriteLine($"Virtual output: {virtualOutput.FriendlyName}");
Console.WriteLine($"Monitor:        {(monitor is null ? "disabled" : monitor.FriendlyName)}");
Console.WriteLine("Select 'CABLE Output' as microphone in Discord/Telegram.");
Console.WriteLine("Press Ctrl+C to stop.");
if (!string.IsNullOrWhiteSpace(options.SoundFile))
{
    Console.WriteLine($"Sound file:     {options.SoundFile}");
    Console.WriteLine("Runtime keys:   S = play sound, X = stop sound");
}

using var engine = new Gate0AudioEngine(input, virtualOutput, monitor, settings);
using var soundPlayer = string.IsNullOrWhiteSpace(options.SoundFile)
    ? null
    : new OneShotSoundPlayer(virtualOutput, monitor, options.SoundVirtualVolume, options.SoundMonitorVolume);
using var done = new ManualResetEventSlim(false);

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    done.Set();
};

try
{
    engine.Start();
    if (soundPlayer is not null)
    {
        StartKeyboardLoop(soundPlayer, options.SoundFile!, done);
    }

    if (options.DurationSeconds > 0)
    {
        done.Wait(TimeSpan.FromSeconds(options.DurationSeconds));
    }
    else
    {
        done.Wait();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal audio error: {ex.Message}");
    return 10;
}
finally
{
    engine.Stop();
}

return 0;

static void PrintDevices(AudioDeviceCatalog catalog)
{
    Console.WriteLine("Capture devices / microphones:");
    foreach (var device in catalog.ListCaptureDevices())
    {
        Console.WriteLine($"  [{device.State}] {device.FriendlyName}");
        Console.WriteLine($"      {device.Id}");
    }

    Console.WriteLine();
    Console.WriteLine("Render devices / outputs:");
    foreach (var device in catalog.ListRenderDevices())
    {
        Console.WriteLine($"  [{device.State}] {device.FriendlyName}");
        Console.WriteLine($"      {device.Id}");
    }
}

static void PrintHelp()
{
    Console.WriteLine("VoiSe Gate 0 CLI");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/VoiSe.Gate0.Cli -- --list-devices");
    Console.WriteLine("  dotnet run --project src/VoiSe.Gate0.Cli -- --input \"Microphone\" --virtual-output \"CABLE Input\" --monitor \"Headphones\"");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --list-devices");
    Console.WriteLine("  --input <name-or-id>");
    Console.WriteLine("  --virtual-output <name-or-id>       default: CABLE Input");
    Console.WriteLine("  --monitor <name-or-id>");
    Console.WriteLine("  --duration <seconds>                default: 0, run until Ctrl+C");
    Console.WriteLine("  --sound-file <path>                 optional WAV/MP3/OGG one-shot file");
    Console.WriteLine("  --sound-virtual-volume <value>      default: 1.0");
    Console.WriteLine("  --sound-monitor-volume <value>      default: 1.0");
    Console.WriteLine("  --input-gain-db <value>             default: 0");
    Console.WriteLine("  --voice-gain-db <value>             default: 0");
    Console.WriteLine("  --gate-threshold-db <value>         default: -45");
    Console.WriteLine("  --compressor-threshold-db <value>   default: -18");
    Console.WriteLine("  --compressor-ratio <value>          default: 3");
    Console.WriteLine("  --limiter-ceiling-db <value>        default: -1");
    Console.WriteLine("  --disable-gate");
    Console.WriteLine("  --disable-compressor");
    Console.WriteLine("  --disable-limiter");
}


static void StartKeyboardLoop(OneShotSoundPlayer soundPlayer, string soundFile, ManualResetEventSlim done)
{
    var thread = new Thread(() =>
    {
        while (!done.IsSet)
        {
            if (!Console.KeyAvailable)
            {
                Thread.Sleep(50);
                continue;
            }

            var key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.S)
            {
                try
                {
                    soundPlayer.Play(soundFile);
                    Console.WriteLine($"Played sound: {soundFile}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Sound playback error: {ex.Message}");
                }
            }
            else if (key == ConsoleKey.X)
            {
                soundPlayer.Stop();
                Console.WriteLine("Sound stopped.");
            }
        }
    })
    {
        IsBackground = true,
        Name = "VoiSe Gate1 keyboard loop"
    };

    thread.Start();
}
