using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using VoiSe.Audio;

namespace VoiSe.App;

/// <summary>
/// UI-side proxy for the isolated VoiSe.AudioHost.exe process. The WinUI process
/// owns no microphone capture, monitor render or VB-CABLE render sessions.
/// </summary>
internal sealed class AudioEngineClient : IDisposable
{
    private readonly string _inputDeviceId;
    private readonly string _virtualOutputDeviceId;
    private readonly string? _monitorDeviceId;
    private readonly EffectSettings _initialSettings;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly CancellationTokenSource _pollCancellation = new();
    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Process? _hostProcess;
    private Task? _pollTask;
    private volatile AudioEngineSnapshot _snapshot = new();
    private bool _started;
    private bool _disposed;

    public AudioEngineClient(
        string inputDeviceId,
        string virtualOutputDeviceId,
        string? monitorDeviceId,
        EffectSettings initialSettings)
    {
        _inputDeviceId = inputDeviceId;
        _virtualOutputDeviceId = virtualOutputDeviceId;
        _monitorDeviceId = monitorDeviceId;
        _initialSettings = initialSettings;
    }

    public int? HostProcessId => _hostProcess?.HasExited == false ? _hostProcess.Id : null;
    public AudioEngineStartResult? StartResult { get; private set; }
    public bool VoiceMonitorRouteEnabled => _snapshot.VoiceMonitorRouteEnabled;
    public bool IsMediaBridgeBroadcasting => _snapshot.IsMediaBridgeBroadcasting;
    public bool IsMediaBridgePaused => _snapshot.IsMediaBridgePaused;
    public float MediaBridgeSourcePeak => _snapshot.MediaBridgeSourcePeak;
    public float MediaBridgeOutputPeak => _snapshot.MediaBridgeOutputPeak;
    public string? MediaBridgeError => _snapshot.MediaBridgeError ?? _snapshot.HostError;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("The isolated Audio Host is already running.");
        }

        var hostPath = ResolveAudioHostPath();
        var pipeName = $"VoiSee.AudioEngine.{Environment.ProcessId}.{Guid.NewGuid():N}";
        _pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        try
        {
            _hostProcess = DetachedAudioHostLauncher.StartWithExplorerParent(
                hostPath,
                $"--pipe \"{pipeName}\"");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            _pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();

            _reader = new StreamReader(_pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            _writer = new StreamWriter(_pipe, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            StartResult = Send<AudioEngineStartRequest, AudioEngineStartResult>(
                "StartEngine",
                new AudioEngineStartRequest
                {
                    InputDeviceId = _inputDeviceId,
                    VirtualOutputDeviceId = _virtualOutputDeviceId,
                    MonitorDeviceId = _monitorDeviceId,
                    Settings = _initialSettings
                });

            _started = true;
            RefreshSnapshot();
            _pollTask = Task.Run(PollSnapshotLoopAsync);
        }
        catch
        {
            DisposeTransport(forceHostExit: true);
            throw;
        }
    }

    public Task StartMediaBridgeAsync(int processId)
    {
        return SendAsync("StartMediaBridge", new AudioEngineProcessRequest { ProcessId = processId });
    }

    public void StopMediaBridge() => Send("StopMediaBridge");

    public bool ToggleMediaBridgePause()
    {
        var result = Send<AudioEngineSoundKeyRequest, bool>("ToggleMediaBridgePause", new AudioEngineSoundKeyRequest());
        RefreshSnapshotBestEffort();
        return result;
    }

    public void SetMediaBridgePaused(bool paused)
    {
        Send("SetMediaBridgePaused", new AudioEngineBoolRequest { Value = paused });
        RefreshSnapshotBestEffort();
    }

    public void UpdateMediaBridgeVolume(float volume) =>
        Send("UpdateMediaBridgeVolume", new AudioEngineFloatRequest { Value = volume });

    public void PlaySound(
        string filePath,
        float virtualVolume,
        float monitorVolume,
        int virtualDelayMs,
        bool loop = false,
        string? playbackKey = null)
    {
        Send("PlaySound", new AudioEnginePlaySoundRequest
        {
            FilePath = filePath,
            VirtualVolume = virtualVolume,
            MonitorVolume = monitorVolume,
            VirtualDelayMs = virtualDelayMs,
            Loop = loop,
            PlaybackKey = playbackKey
        });
    }

    public Task PreloadSoundAsync(string filePath)
    {
        return SendAsync("PreloadSound", new AudioEnginePlaySoundRequest { FilePath = filePath });
    }

    public void StopSound(string? playbackKey = null) =>
        Send("StopSound", new AudioEngineSoundKeyRequest { PlaybackKey = playbackKey });

    public void SeekSound(double seconds, string? playbackKey = null) =>
        Send("SeekSound", new AudioEngineSeekSoundRequest { Seconds = seconds, PlaybackKey = playbackKey });

    public bool ToggleSoundPause(string? playbackKey = null) =>
        Send<AudioEngineSoundKeyRequest, bool>(
            "ToggleSoundPause",
            new AudioEngineSoundKeyRequest { PlaybackKey = playbackKey });

    public SoundboardStatus GetSoundStatus(string? playbackKey = null) =>
        Send<AudioEngineSoundKeyRequest, SoundboardStatus>(
            "GetSoundStatus",
            new AudioEngineSoundKeyRequest { PlaybackKey = playbackKey });

    public void UpdateEffectSettings(EffectSettings settings) =>
        Send("UpdateEffectSettings", settings);

    public void UpdateSoundVolumes(float virtualVolume, float monitorVolume, string? playbackKey = null) =>
        Send("UpdateSoundVolumes", new AudioEngineSoundVolumesRequest
        {
            VirtualVolume = virtualVolume,
            MonitorVolume = monitorVolume,
            PlaybackKey = playbackKey
        });

    public void SetVirtualMicMuted(bool muted) =>
        Send("SetVirtualMicMuted", new AudioEngineBoolRequest { Value = muted });

    public void UpdateSoundLoop(bool loop, string? playbackKey = null) =>
        Send("UpdateSoundLoop", new AudioEngineSoundLoopRequest { Loop = loop, PlaybackKey = playbackKey });

    private async Task PollSnapshotLoopAsync()
    {
        while (!_pollCancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(40, _pollCancellation.Token).ConfigureAwait(false);
                var snapshot = await SendAsync<AudioEngineSnapshot>("GetSnapshot", _pollCancellation.Token)
                    .ConfigureAwait(false);
                _snapshot = snapshot;
            }
            catch (OperationCanceledException) when (_pollCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _snapshot = new AudioEngineSnapshot
                {
                    Running = false,
                    HostError = ex.Message
                };
                return;
            }
        }
    }

    private void RefreshSnapshot()
    {
        _snapshot = Send<object, AudioEngineSnapshot>("GetSnapshot", new { });
    }

    private void RefreshSnapshotBestEffort()
    {
        try
        {
            RefreshSnapshot();
        }
        catch
        {
            // The polling loop will surface persistent transport failure.
        }
    }

    private void Send(string command)
    {
        _ = Send<object, object?>(command, new { });
    }

    private void Send<TPayload>(string command, TPayload payload)
    {
        _ = Send<TPayload, object?>(command, payload);
    }

    private TResult Send<TPayload, TResult>(string command, TPayload payload)
    {
        return SendAsync<TPayload, TResult>(command, payload, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private Task SendAsync<TPayload>(string command, TPayload payload)
    {
        return SendAsync<TPayload, object?>(command, payload, CancellationToken.None);
    }

    private Task<TResult> SendAsync<TResult>(string command, CancellationToken cancellationToken)
    {
        return SendRequestAsync<TResult>(AudioEngineRequestEnvelope.Create(command), cancellationToken);
    }

    private Task<TResult> SendAsync<TPayload, TResult>(
        string command,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        return SendRequestAsync<TResult>(AudioEngineRequestEnvelope.Create(command, payload), cancellationToken);
    }

    private async Task<TResult> SendRequestAsync<TResult>(
        AudioEngineRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var writer = _writer ?? throw new InvalidOperationException("Audio Host pipe is not connected.");
        var reader = _reader ?? throw new InvalidOperationException("Audio Host pipe is not connected.");

        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, AudioEngineJson.Options))
                .ConfigureAwait(false);

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new EndOfStreamException("VoiSe.AudioHost.exe closed the control pipe.");
            }

            var response = JsonSerializer.Deserialize<AudioEngineResponseEnvelope>(line, AudioEngineJson.Options)
                ?? throw new InvalidDataException("Audio Host returned an empty response.");
            if (response.RequestId != request.RequestId && response.RequestId != Guid.Empty)
            {
                throw new InvalidDataException("Audio Host response ID did not match the request.");
            }

            if (!response.Success)
            {
                throw new InvalidOperationException(response.Error ?? "Audio Host command failed.");
            }

            if (typeof(TResult) == typeof(object))
            {
                return default!;
            }

            if (string.IsNullOrWhiteSpace(response.PayloadJson))
            {
                return default!;
            }

            return JsonSerializer.Deserialize<TResult>(response.PayloadJson, AudioEngineJson.Options)!;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static string ResolveAudioHostPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AudioHost", "VoiSe.AudioHost.exe");
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException(
            "VoiSe.AudioHost.exe was not found. Rebuild the complete VoiSee project so the AudioHost folder is copied next to VoiSe.App.exe.",
            path);
    }

    private void DisposeTransport(bool forceHostExit)
    {
        _pollCancellation.Cancel();
        try
        {
            _pollTask?.Wait(600);
        }
        catch
        {
            // Best-effort shutdown.
        }

        if (_writer is not null && _reader is not null && _pipe?.IsConnected == true)
        {
            try
            {
                var shutdown = AudioEngineRequestEnvelope.Create("Shutdown");
                var lockTaken = _requestLock.Wait(500);
                if (lockTaken)
                {
                    try
                    {
                        _writer.WriteLine(JsonSerializer.Serialize(shutdown, AudioEngineJson.Options));
                        _reader.ReadLine();
                    }
                    finally
                    {
                        _requestLock.Release();
                    }
                }
            }
            catch
            {
                // Host may already be gone.
            }
        }

        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }

        var process = _hostProcess;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited && !process.WaitForExit(1500) && forceHostExit)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1000);
                }
            }
            catch
            {
                // Process can disappear between checks.
            }
            finally
            {
                process.Dispose();
            }
        }

        _writer = null;
        _reader = null;
        _pipe = null;
        _hostProcess = null;
        _pollTask = null;
        _started = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeTransport(forceHostExit: true);
        _pollCancellation.Dispose();
        _requestLock.Dispose();
        _disposed = true;
    }
}
