using System.IO.Pipes;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiSe.Audio;

/// <summary>
/// Hidden helper mode used by the VoiSee executable. The helper owns the WASAPI
/// render session that writes the final VoiSee mix into VB-CABLE. The UI process
/// sends already-mixed 48 kHz stereo float samples through a named pipe.
/// </summary>
public static class VirtualMicOutputHost
{
    private const string HostSwitch = "--virtual-mic-host";
    private const string PipeSwitch = "--pipe";
    private const string ProtocolMagic = "VOISEE-VIRTUAL-MIC-HOST";
    private const int ProtocolVersion = 1;

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!args.Any(argument =>
                string.Equals(argument, HostSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var pipeName = ReadArgumentValue(args, PipeSwitch);
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            WriteHostLog("Virtual microphone host was started without a pipe name.");
            exitCode = 2;
            return true;
        }

        try
        {
            Run(pipeName);
            exitCode = 0;
        }
        catch (Exception ex)
        {
            WriteHostLog("Virtual microphone host fatal error: " + ex);
            exitCode = 1;
        }

        return true;
    }

    internal static string BuildArguments(string pipeName)
    {
        return $"{HostSwitch} {PipeSwitch} \"{pipeName.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
    }

    private static void Run(string pipeName)
    {
        using var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.None);
        pipe.Connect(timeout: 8_000);

        using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
        var magic = reader.ReadString();
        var version = reader.ReadInt32();
        if (!string.Equals(magic, ProtocolMagic, StringComparison.Ordinal) || version != ProtocolVersion)
        {
            throw new InvalidDataException("Unsupported isolated virtual microphone pipe protocol.");
        }

        var sampleRate = reader.ReadInt32();
        var channels = reader.ReadInt32();
        var deviceId = reader.ReadString();
        if (sampleRate <= 0 || channels <= 0 || string.IsNullOrWhiteSpace(deviceId))
        {
            throw new InvalidDataException("The isolated virtual microphone handshake is incomplete.");
        }

        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        var bufferedProvider = new BufferedWaveProvider(format)
        {
            BufferDuration = TimeSpan.FromMilliseconds(600),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

        using var enumerator = new MMDeviceEnumerator();
        using var outputDevice = enumerator.GetDevice(deviceId);
        using var output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 50);
        output.Init(bufferedProvider);
        output.Play();

        using var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);
        writer.Write(true);
        writer.Flush();

        WriteHostLog($"Isolated virtual microphone host started. PID={Environment.ProcessId}; device={outputDevice.FriendlyName}.");

        while (true)
        {
            int byteCount;
            try
            {
                byteCount = reader.ReadInt32();
            }
            catch (EndOfStreamException)
            {
                break;
            }

            if (byteCount == 0)
            {
                break;
            }

            if (byteCount < 0 || byteCount > 1024 * 1024)
            {
                throw new InvalidDataException("Invalid isolated virtual microphone audio block length.");
            }

            var block = reader.ReadBytes(byteCount);
            if (block.Length != byteCount)
            {
                throw new EndOfStreamException("The isolated virtual microphone pipe closed inside an audio block.");
            }

            bufferedProvider.AddSamples(block, 0, block.Length);
        }

        output.Stop();
        WriteHostLog("Isolated virtual microphone host stopped normally.");
    }

    internal static void WriteHandshake(BinaryWriter writer, WaveFormat format, string deviceId)
    {
        writer.Write(ProtocolMagic);
        writer.Write(ProtocolVersion);
        writer.Write(format.SampleRate);
        writer.Write(format.Channels);
        writer.Write(deviceId);
        writer.Flush();
    }

    private static string? ReadArgumentValue(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void WriteHostLog(string message)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoiSe");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "virtual-mic-host.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort diagnostics only.
        }
    }
}
