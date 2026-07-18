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

/// <summary>
/// Produces read-only snapshots of active Windows Core Audio endpoints and
/// their render sessions. This service never changes endpoint or session volume.
/// </summary>
public sealed class AudioDiagnosticsService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _disposed;

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

        _enumerator.Dispose();
        _disposed = true;
    }
}
