using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace VoiSe.Audio;

public sealed record AudioSessionDiagnosticInfo(
    uint ProcessId,
    string ProcessName,
    string DisplayName,
    string State,
    float Peak,
    float Volume,
    bool Muted,
    bool IsSystemSounds);

public sealed record AudioEndpointDiagnosticInfo(
    string Id,
    string FriendlyName,
    string DataFlow,
    string State,
    float Peak,
    float Volume,
    bool Muted,
    IReadOnlyList<string> DefaultRoles,
    IReadOnlyList<AudioSessionDiagnosticInfo> Sessions,
    string? Error);

public sealed record AudioDiagnosticsSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<AudioEndpointDiagnosticInfo> Endpoints,
    string? Error);

public sealed record AudioSessionMuteOperationResult(
    int MatchedSessions,
    int ChangedSessions,
    bool MuteRequested,
    string Message);

/// <summary>
/// Produces snapshots of active Windows Core Audio endpoints and render sessions.
/// It also contains one deliberately narrow diagnostic action: temporarily mute
/// only Discord render sessions on the VB-CABLE "CABLE Input" endpoint.
/// </summary>
public sealed class AudioDiagnosticsService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _discordCableMuteSync = new();
    private readonly Dictionary<string, bool> _discordCableOriginalMuteStates =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _discordCableMuteArmed;
    private bool _disposed;

    public bool DiscordCableInputMuteArmed
    {
        get
        {
            lock (_discordCableMuteSync)
            {
                return _discordCableMuteArmed;
            }
        }
    }

    public AudioDiagnosticsSnapshot CaptureSnapshot()
    {
        if (_disposed)
        {
            return new AudioDiagnosticsSnapshot(
                DateTimeOffset.Now,
                Array.Empty<AudioEndpointDiagnosticInfo>(),
                "Audio diagnostics service is disposed.");
        }

        try
        {
            var endpoints = new List<AudioEndpointDiagnosticInfo>();
            endpoints.AddRange(CaptureFlow(DataFlow.Render));
            endpoints.AddRange(CaptureFlow(DataFlow.Capture));

            return new AudioDiagnosticsSnapshot(
                DateTimeOffset.Now,
                endpoints
                    .OrderBy(endpoint => endpoint.DataFlow, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(endpoint => endpoint.FriendlyName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                null);
        }
        catch (Exception ex)
        {
            return new AudioDiagnosticsSnapshot(
                DateTimeOffset.Now,
                Array.Empty<AudioEndpointDiagnosticInfo>(),
                ex.Message);
        }
    }

    /// <summary>
    /// Arms or disarms the narrow Discord-on-CABLE diagnostic mute.
    /// When armed, only render sessions whose process is Discord and whose endpoint
    /// is exactly the normal "CABLE Input" endpoint are muted. Discord sessions on
    /// physical headphones and the VoiSee session on CABLE Input are not changed.
    /// Original session mute states are remembered and restored when disarmed.
    /// </summary>
    public AudioSessionMuteOperationResult SetDiscordCableInputSessionMuted(bool muted)
    {
        lock (_discordCableMuteSync)
        {
            if (_disposed)
            {
                return new AudioSessionMuteOperationResult(0, 0, muted, "Audio diagnostics service is disposed.");
            }

            _discordCableMuteArmed = muted;
            return ApplyDiscordCableInputSessionMute(muted, restoreOriginalState: !muted);
        }
    }

    /// <summary>
    /// Re-applies the armed mute to sessions created or recreated by Discord while
    /// the diagnostic dialog is open.
    /// </summary>
    public AudioSessionMuteOperationResult RefreshDiscordCableInputSessionMute()
    {
        lock (_discordCableMuteSync)
        {
            if (_disposed || !_discordCableMuteArmed)
            {
                return new AudioSessionMuteOperationResult(0, 0, false, "Discord CABLE Input mute is not armed.");
            }

            return ApplyDiscordCableInputSessionMute(muted: true, restoreOriginalState: false);
        }
    }

    private AudioSessionMuteOperationResult ApplyDiscordCableInputSessionMute(
        bool muted,
        bool restoreOriginalState)
    {
        var matched = 0;
        var changed = 0;
        var endpointFound = false;

        try
        {
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                using var device = devices[deviceIndex];
                var friendlyName = SafeRead(() => device.FriendlyName, string.Empty);
                if (!IsNormalCableInputEndpoint(friendlyName))
                {
                    continue;
                }

                endpointFound = true;
                var sessions = device.AudioSessionManager.Sessions;
                if (sessions is null)
                {
                    continue;
                }

                for (var sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
                {
                    using var session = sessions[sessionIndex];
                    var processId = ReadOrDefault(() => session.GetProcessID);
                    if (!ResolveProcessName(processId).Equals("Discord", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var volume = session.SimpleAudioVolume;
                    if (volume is null)
                    {
                        continue;
                    }

                    matched++;
                    var sessionKey = BuildSessionKey(device.ID, session, processId);
                    var currentMuted = ReadOrDefault(() => volume.Mute);

                    if (muted)
                    {
                        if (!_discordCableOriginalMuteStates.ContainsKey(sessionKey))
                        {
                            _discordCableOriginalMuteStates[sessionKey] = currentMuted;
                        }

                        if (!currentMuted)
                        {
                            volume.Mute = true;
                            changed++;
                        }
                    }
                    else if (restoreOriginalState
                             && _discordCableOriginalMuteStates.TryGetValue(sessionKey, out var originalMuted)
                             && currentMuted != originalMuted)
                    {
                        volume.Mute = originalMuted;
                        changed++;
                    }
                }
            }

            if (!muted && restoreOriginalState)
            {
                _discordCableOriginalMuteStates.Clear();
            }

            var message = !endpointFound
                ? "CABLE Input render endpoint was not found."
                : matched == 0
                    ? "No Discord render session is currently present on CABLE Input."
                    : muted
                        ? $"Discord on CABLE Input: {matched} session(s) matched, {changed} newly muted."
                        : $"Discord on CABLE Input: {matched} session(s) matched, {changed} restored.";

            return new AudioSessionMuteOperationResult(matched, changed, muted, message);
        }
        catch (Exception ex)
        {
            if (!muted && restoreOriginalState)
            {
                _discordCableOriginalMuteStates.Clear();
            }

            return new AudioSessionMuteOperationResult(
                matched,
                changed,
                muted,
                $"Discord CABLE Input session mute error: {ex.Message}");
        }
    }

    private IReadOnlyList<AudioEndpointDiagnosticInfo> CaptureFlow(DataFlow flow)
    {
        var result = new List<AudioEndpointDiagnosticInfo>();
        var defaultRoleIds = ResolveDefaultRoleIds(flow);
        var devices = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);

        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            try
            {
                var roles = defaultRoleIds
                    .Where(pair => pair.Value.Equals(device.ID, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Key)
                    .ToList();

                var peak = ReadOrDefault(() => device.AudioMeterInformation.MasterPeakValue);
                var volume = ReadOrDefault(() => device.AudioEndpointVolume.MasterVolumeLevelScalar, 1.0f);
                var muted = ReadOrDefault(() => device.AudioEndpointVolume.Mute);
                var sessions = flow == DataFlow.Render
                    ? CaptureSessions(device)
                    : Array.Empty<AudioSessionDiagnosticInfo>();

                result.Add(new AudioEndpointDiagnosticInfo(
                    device.ID,
                    device.FriendlyName,
                    flow.ToString(),
                    device.State.ToString(),
                    peak,
                    volume,
                    muted,
                    roles,
                    sessions,
                    null));
            }
            catch (Exception ex)
            {
                result.Add(new AudioEndpointDiagnosticInfo(
                    SafeRead(() => device.ID, "Unknown endpoint id"),
                    SafeRead(() => device.FriendlyName, "Unknown endpoint"),
                    flow.ToString(),
                    SafeRead(() => device.State.ToString(), "Unknown"),
                    0,
                    0,
                    false,
                    Array.Empty<string>(),
                    Array.Empty<AudioSessionDiagnosticInfo>(),
                    ex.Message));
            }
            finally
            {
                device.Dispose();
            }
        }

        return result;
    }

    private IReadOnlyList<AudioSessionDiagnosticInfo> CaptureSessions(MMDevice device)
    {
        var result = new List<AudioSessionDiagnosticInfo>();

        try
        {
            var manager = device.AudioSessionManager;
            var sessions = manager.Sessions;
            if (sessions is null)
            {
                return result;
            }

            for (var index = 0; index < sessions.Count; index++)
            {
                using var session = sessions[index];
                try
                {
                    var processId = ReadOrDefault(() => session.GetProcessID);
                    var isSystem = ReadOrDefault(() => session.IsSystemSoundsSession);
                    var processName = isSystem
                        ? "System Sounds"
                        : ResolveProcessName(processId);
                    var displayName = SafeRead(() => session.DisplayName, string.Empty);
                    var state = SafeRead(() => session.State.ToString(), "Unknown");
                    var peak = session.AudioMeterInformation is null
                        ? 0
                        : ReadOrDefault(() => session.AudioMeterInformation.MasterPeakValue);
                    var volume = session.SimpleAudioVolume is null
                        ? 1
                        : ReadOrDefault(() => session.SimpleAudioVolume.Volume, 1.0f);
                    var muted = session.SimpleAudioVolume is not null
                        && ReadOrDefault(() => session.SimpleAudioVolume.Mute);

                    result.Add(new AudioSessionDiagnosticInfo(
                        processId,
                        processName,
                        displayName,
                        state,
                        peak,
                        volume,
                        muted,
                        isSystem));
                }
                catch (Exception ex)
                {
                    result.Add(new AudioSessionDiagnosticInfo(
                        0,
                        "Unavailable session",
                        ex.Message,
                        "Error",
                        0,
                        0,
                        false,
                        false));
                }
            }
        }
        catch
        {
            // Some drivers expose an active render endpoint but do not allow
            // session enumeration. Endpoint diagnostics are still useful.
        }

        return result
            .OrderByDescending(session => session.Peak)
            .ThenBy(session => session.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Dictionary<string, string> ResolveDefaultRoleIds(DataFlow flow)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddDefaultRole(result, flow, Role.Console, "Console");
        AddDefaultRole(result, flow, Role.Multimedia, "Multimedia");
        AddDefaultRole(result, flow, Role.Communications, "Communications");
        return result;
    }

    private void AddDefaultRole(Dictionary<string, string> target, DataFlow flow, Role role, string name)
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(flow, role);
            target[name] = device.ID;
        }
        catch
        {
            // A role can legitimately have no default endpoint.
        }
    }

    private static bool IsNormalCableInputEndpoint(string friendlyName)
    {
        return friendlyName.StartsWith("CABLE Input", StringComparison.OrdinalIgnoreCase)
               && !friendlyName.StartsWith("CABLE In 16ch", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSessionKey(string endpointId, AudioSessionControl session, uint processId)
    {
        var instanceId = SafeRead(() => session.GetSessionInstanceIdentifier, string.Empty);
        return string.IsNullOrWhiteSpace(instanceId)
            ? $"{endpointId}|PID:{processId}"
            : $"{endpointId}|{instanceId}";
    }

    private static string ResolveProcessName(uint processId)
    {
        if (processId == 0)
        {
            return "System";
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return $"PID {processId}";
        }
    }

    private static T ReadOrDefault<T>(Func<T> reader, T fallback = default!)
    {
        try
        {
            return reader();
        }
        catch
        {
            return fallback;
        }
    }

    private static string SafeRead(Func<string> reader, string fallback)
    {
        try
        {
            return reader();
        }
        catch
        {
            return fallback;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_discordCableMuteSync)
        {
            if (_discordCableMuteArmed)
            {
                try
                {
                    _discordCableMuteArmed = false;
                    ApplyDiscordCableInputSessionMute(muted: false, restoreOriginalState: true);
                }
                catch
                {
                    // Best-effort restore during shutdown.
                }
            }
        }

        _enumerator.Dispose();
        _disposed = true;
    }
}
