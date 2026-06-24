internal sealed class CommandLineOptions
{
    public bool ShowHelp { get; private init; }
    public bool ListDevices { get; private init; }
    public string? InputQuery { get; private init; }
    public string? VirtualOutputQuery { get; private init; }
    public string? MonitorQuery { get; private init; }
    public string? SoundFile { get; private init; }
    public float SoundVirtualVolume { get; private init; } = 1.0f;
    public float SoundMonitorVolume { get; private init; } = 1.0f;
    public int DurationSeconds { get; private init; }
    public float InputGainDb { get; private init; }
    public float VoiceGainDb { get; private init; }
    public float GateThresholdDb { get; private init; } = -45.0f;
    public float CompressorThresholdDb { get; private init; } = -18.0f;
    public float CompressorRatio { get; private init; } = 3.0f;
    public float LimiterCeilingDb { get; private init; } = -1.0f;
    public bool DisableGate { get; private init; }
    public bool DisableCompressor { get; private init; }
    public bool DisableLimiter { get; private init; }

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new MutableOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--list-devices":
                    options.ListDevices = true;
                    break;
                case "--input":
                    options.InputQuery = ReadValue(args, ref i, arg);
                    break;
                case "--virtual-output":
                    options.VirtualOutputQuery = ReadValue(args, ref i, arg);
                    break;
                case "--monitor":
                    options.MonitorQuery = ReadValue(args, ref i, arg);
                    break;
                case "--sound-file":
                    options.SoundFile = ReadValue(args, ref i, arg);
                    break;
                case "--sound-virtual-volume":
                    options.SoundVirtualVolume = ParseFloat(ReadValue(args, ref i, arg), arg);
                    break;
                case "--sound-monitor-volume":
                    options.SoundMonitorVolume = ParseFloat(ReadValue(args, ref i, arg), arg);
                    break;
                case "--duration":
                    options.DurationSeconds = ParseInt(ReadValue(args, ref i, arg), arg);
                    break;
                case "--input-gain-db":
                    options.InputGainDb = ParseFloat(ReadValue(args, ref i, arg), arg);
                    break;
                case "--voice-gain-db":
                    options.VoiceGainDb = ParseFloat(ReadValue(args, ref i, arg), arg);
                    break;
                case "--gate-threshold-db":
                    options.GateThresholdDb = ParseFloat(ReadValue(args, ref i, arg), arg);
                    break;
                case "--compressor-threshold-db":
                    options.CompressorThresholdDb = ParseFloat(ReadValue(args, ref i, arg), arg);
                    break;
                case "--compressor-ratio":
                    options.CompressorRatio = ParseFloat(ReadValue(args, ref i, arg), arg);
                    break;
                case "--limiter-ceiling-db":
                    options.LimiterCeilingDb = ParseFloat(ReadValue(args, ref i, arg), arg);
                    break;
                case "--disable-gate":
                    options.DisableGate = true;
                    break;
                case "--disable-compressor":
                    options.DisableCompressor = true;
                    break;
                case "--disable-limiter":
                    options.DisableLimiter = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return options.ToImmutable();
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Option {option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static int ParseInt(string value, string option)
    {
        if (!int.TryParse(value, out var result))
        {
            throw new ArgumentException($"Option {option} expects integer value.");
        }

        return result;
    }

    private static float ParseFloat(string value, string option)
    {
        if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"Option {option} expects numeric value. Use dot as decimal separator.");
        }

        return result;
    }

    private sealed class MutableOptions
    {
        public bool ShowHelp { get; set; }
        public bool ListDevices { get; set; }
        public string? InputQuery { get; set; }
        public string? VirtualOutputQuery { get; set; }
        public string? MonitorQuery { get; set; }
        public string? SoundFile { get; set; }
        public float SoundVirtualVolume { get; set; } = 1.0f;
        public float SoundMonitorVolume { get; set; } = 1.0f;
        public int DurationSeconds { get; set; }
        public float InputGainDb { get; set; }
        public float VoiceGainDb { get; set; }
        public float GateThresholdDb { get; set; } = -45.0f;
        public float CompressorThresholdDb { get; set; } = -18.0f;
        public float CompressorRatio { get; set; } = 3.0f;
        public float LimiterCeilingDb { get; set; } = -1.0f;
        public bool DisableGate { get; set; }
        public bool DisableCompressor { get; set; }
        public bool DisableLimiter { get; set; }

        public CommandLineOptions ToImmutable() => new()
        {
            ShowHelp = ShowHelp,
            ListDevices = ListDevices,
            InputQuery = InputQuery,
            VirtualOutputQuery = VirtualOutputQuery,
            MonitorQuery = MonitorQuery,
            SoundFile = SoundFile,
            SoundVirtualVolume = SoundVirtualVolume,
            SoundMonitorVolume = SoundMonitorVolume,
            DurationSeconds = DurationSeconds,
            InputGainDb = InputGainDb,
            VoiceGainDb = VoiceGainDb,
            GateThresholdDb = GateThresholdDb,
            CompressorThresholdDb = CompressorThresholdDb,
            CompressorRatio = CompressorRatio,
            LimiterCeilingDb = LimiterCeilingDb,
            DisableGate = DisableGate,
            DisableCompressor = DisableCompressor,
            DisableLimiter = DisableLimiter
        };
    }
}
