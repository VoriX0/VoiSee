using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using VoiSe.Audio;

namespace VoiSe.AudioHost;

internal static class Program
{
    private const string PipeArgument = "--pipe";
    private const string LogFileName = "audio-host.log";

    [STAThread]
    private static int Main(string[] args)
    {
        var pipeName = ReadArgument(args, PipeArgument);
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            WriteLog("Audio Host started without --pipe.");
            return 2;
        }

        try
        {
            WriteLog($"Audio Host process started. PID={Environment.ProcessId}; executable={Environment.ProcessPath}.");
            Run(pipeName);
            WriteLog("Audio Host process stopped normally.");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLog("Audio Host fatal error: " + ex);
            return 1;
        }
    }

    private static void Run(string pipeName)
    {
        using var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.None);
        pipe.Connect(timeout: 10_000);

        using var reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = true
        };

        using var session = new AudioHostSession();
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                return;
            }

            AudioEngineRequestEnvelope? request;
            try
            {
                request = JsonSerializer.Deserialize<AudioEngineRequestEnvelope>(line, AudioEngineJson.Options);
            }
            catch (Exception ex)
            {
                WriteResponse(writer, AudioEngineResponseEnvelope.Fail(Guid.Empty, ex));
                continue;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Command))
            {
                WriteResponse(writer, AudioEngineResponseEnvelope.Fail(
                    request?.RequestId ?? Guid.Empty,
                    new InvalidDataException("Audio Host received an empty command.")));
                continue;
            }

            var shouldExit = false;
            AudioEngineResponseEnvelope response;
            try
            {
                response = session.ExecuteAsync(request).GetAwaiter().GetResult();
                shouldExit = string.Equals(request.Command, "Shutdown", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                response = AudioEngineResponseEnvelope.Fail(request.RequestId, ex);
                WriteLog($"Command {request.Command} failed: {ex}");
            }

            WriteResponse(writer, response);
            if (shouldExit)
            {
                return;
            }
        }
    }

    private static void WriteResponse(StreamWriter writer, AudioEngineResponseEnvelope response)
    {
        writer.WriteLine(JsonSerializer.Serialize(response, AudioEngineJson.Options));
    }

    private static string? ReadArgument(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void WriteLog(string message)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoiSe");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, LogFileName),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never stop the audio host.
        }
    }
}

internal sealed class AudioHostSession : IDisposable
{
    private readonly AudioDeviceCatalog _catalog = new();
    private Gate2UnifiedAudioEngine? _engine;
    private bool _disposed;

    public async Task<AudioEngineResponseEnvelope> ExecuteAsync(AudioEngineRequestEnvelope request)
    {
        switch (request.Command)
        {
            case "StartEngine":
                return StartEngine(request);
            case "UpdateEffectSettings":
                RequireEngine().UpdateEffectSettings(AudioEngineJson.DeserializePayload<EffectSettings>(request.PayloadJson));
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            case "SetVirtualMicMuted":
                RequireEngine().SetVirtualMicMuted(AudioEngineJson.DeserializePayload<AudioEngineBoolRequest>(request.PayloadJson).Value);
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            case "UpdateMediaBridgeVolume":
                RequireEngine().UpdateMediaBridgeVolume(AudioEngineJson.DeserializePayload<AudioEngineFloatRequest>(request.PayloadJson).Value);
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            case "StartMediaBridge":
                await RequireEngine().StartMediaBridgeAsync(AudioEngineJson.DeserializePayload<AudioEngineProcessRequest>(request.PayloadJson).ProcessId)
                    .ConfigureAwait(false);
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            case "StopMediaBridge":
                RequireEngine().StopMediaBridge();
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            case "ToggleMediaBridgePause":
                return AudioEngineResponseEnvelope.Ok(request.RequestId, RequireEngine().ToggleMediaBridgePause());
            case "SetMediaBridgePaused":
                RequireEngine().SetMediaBridgePaused(AudioEngineJson.DeserializePayload<AudioEngineBoolRequest>(request.PayloadJson).Value);
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            case "PlaySound":
            {
                var payload = AudioEngineJson.DeserializePayload<AudioEnginePlaySoundRequest>(request.PayloadJson);
                RequireEngine().PlaySound(
                    payload.FilePath,
                    payload.VirtualVolume,
                    payload.MonitorVolume,
                    payload.VirtualDelayMs,
                    payload.Loop,
                    payload.PlaybackKey);
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            }
            case "PreloadSound":
            {
                var payload = AudioEngineJson.DeserializePayload<AudioEnginePlaySoundRequest>(request.PayloadJson);
                var engine = RequireEngine();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await engine.PreloadSoundAsync(payload.FilePath).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Cache warm-up is opportunistic and must not block the control channel.
                    }
                });
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            }
            case "StopSound":
                RequireEngine().StopSound(AudioEngineJson.DeserializePayload<AudioEngineSoundKeyRequest>(request.PayloadJson).PlaybackKey);
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            case "SeekSound":
            {
                var payload = AudioEngineJson.DeserializePayload<AudioEngineSeekSoundRequest>(request.PayloadJson);
                RequireEngine().SeekSound(payload.Seconds, payload.PlaybackKey);
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            }
            case "ToggleSoundPause":
            {
                var payload = AudioEngineJson.DeserializePayload<AudioEngineSoundKeyRequest>(request.PayloadJson);
                return AudioEngineResponseEnvelope.Ok(request.RequestId, RequireEngine().ToggleSoundPause(payload.PlaybackKey));
            }
            case "GetSoundStatus":
            {
                var payload = AudioEngineJson.DeserializePayload<AudioEngineSoundKeyRequest>(request.PayloadJson);
                return AudioEngineResponseEnvelope.Ok(request.RequestId, RequireEngine().GetSoundStatus(payload.PlaybackKey));
            }
            case "UpdateSoundVolumes":
            {
                var payload = AudioEngineJson.DeserializePayload<AudioEngineSoundVolumesRequest>(request.PayloadJson);
                RequireEngine().UpdateSoundVolumes(payload.VirtualVolume, payload.MonitorVolume, payload.PlaybackKey);
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            }
            case "UpdateSoundLoop":
            {
                var payload = AudioEngineJson.DeserializePayload<AudioEngineSoundLoopRequest>(request.PayloadJson);
                RequireEngine().UpdateSoundLoop(payload.Loop, payload.PlaybackKey);
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            }
            case "GetSnapshot":
                return AudioEngineResponseEnvelope.Ok(request.RequestId, CreateSnapshot());
            case "Shutdown":
                DisposeEngine();
                return AudioEngineResponseEnvelope.Ok(request.RequestId);
            default:
                throw new InvalidOperationException($"Unsupported Audio Host command: {request.Command}");
        }
    }

    private AudioEngineResponseEnvelope StartEngine(AudioEngineRequestEnvelope request)
    {
        if (_engine is not null)
        {
            throw new InvalidOperationException("The Audio Host engine is already running.");
        }

        var payload = AudioEngineJson.DeserializePayload<AudioEngineStartRequest>(request.PayloadJson);
        var input = _catalog.FindCaptureDevice(payload.InputDeviceId)
            ?? throw new InvalidOperationException("The selected microphone is no longer available.");
        var virtualOutput = _catalog.FindRenderDevice(payload.VirtualOutputDeviceId)
            ?? throw new InvalidOperationException("The selected virtual output is no longer available.");
        var monitor = string.IsNullOrWhiteSpace(payload.MonitorDeviceId)
            ? null
            : _catalog.FindRenderDevice(payload.MonitorDeviceId);

        try
        {
            _engine = new Gate2UnifiedAudioEngine(input, virtualOutput, monitor, payload.Settings);
            _engine.Start();
        }
        catch
        {
            _engine?.Dispose();
            _engine = null;
            input.Dispose();
            virtualOutput.Dispose();
            monitor?.Dispose();
            throw;
        }

        return AudioEngineResponseEnvelope.Ok(request.RequestId, new AudioEngineStartResult
        {
            HostProcessId = Environment.ProcessId,
            InputFriendlyName = input.FriendlyName,
            VirtualOutputFriendlyName = virtualOutput.FriendlyName,
            MonitorFriendlyName = monitor?.FriendlyName
        });
    }

    private AudioEngineSnapshot CreateSnapshot()
    {
        var engine = _engine;
        if (engine is null)
        {
            return new AudioEngineSnapshot
            {
                Running = false
            };
        }

        return new AudioEngineSnapshot
        {
            Running = true,
            VoiceMonitorRouteEnabled = engine.VoiceMonitorRouteEnabled,
            IsMediaBridgeBroadcasting = engine.IsMediaBridgeBroadcasting,
            IsMediaBridgePaused = engine.IsMediaBridgePaused,
            MediaBridgeSourcePeak = engine.MediaBridgeSourcePeak,
            MediaBridgeOutputPeak = engine.MediaBridgeOutputPeak,
            MediaBridgeError = engine.MediaBridgeError
        };
    }

    private Gate2UnifiedAudioEngine RequireEngine()
    {
        return _engine ?? throw new InvalidOperationException("The Audio Host engine is not running.");
    }

    private void DisposeEngine()
    {
        try
        {
            _engine?.Dispose();
        }
        finally
        {
            _engine = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeEngine();
        _catalog.Dispose();
        _disposed = true;
    }
}
