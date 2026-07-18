using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace VoiSe.Audio;

public sealed record DiscordCableSessionIsolationResult(
    int MatchedSessions,
    int ChangedSessions,
    string? Error);

/// <summary>
/// Prevents Discord screen sharing from replaying VoiSee's virtual-microphone
/// render stream as a second voice copy. The service mutes only Discord render
/// sessions attached to the normal VB-CABLE "CABLE Input" endpoint.
/// VoiSee's own session on that endpoint, Discord on physical headphones, and
/// the CABLE Output capture endpoint are not modified.
/// </summary>
public sealed class DiscordCableSessionIsolationService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, bool> _originalMuteStates =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _enabled;
    private bool _disposed;

    public DiscordCableSessionIsolationResult Enable()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return new DiscordCableSessionIsolationResult(
                    0,
                    0,
                    "Discord CABLE Input isolation service is disposed.");
            }

            _enabled = true;
            return ApplyMute(muted: true, restoreOriginalState: false);
        }
    }

    public DiscordCableSessionIsolationResult Refresh()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return new DiscordCableSessionIsolationResult(
                    0,
                    0,
                    "Discord CABLE Input isolation service is disposed.");
            }

            if (!_enabled)
            {
                return new DiscordCableSessionIsolationResult(0, 0, null);
            }

            return ApplyMute(muted: true, restoreOriginalState: false);
        }
    }

    private DiscordCableSessionIsolationResult ApplyMute(bool muted, bool restoreOriginalState)
    {
        var matched = 0;
        var changed = 0;

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
                        if (!_originalMuteStates.ContainsKey(sessionKey))
                        {
                            _originalMuteStates[sessionKey] = currentMuted;
                        }

                        if (!currentMuted)
                        {
                            volume.Mute = true;
                            changed++;
                        }
                    }
                    else if (restoreOriginalState
                             && _originalMuteStates.TryGetValue(sessionKey, out var originalMuted)
                             && currentMuted != originalMuted)
                    {
                        volume.Mute = originalMuted;
                        changed++;
                    }
                }
            }

            if (!muted && restoreOriginalState)
            {
                _originalMuteStates.Clear();
            }

            return new DiscordCableSessionIsolationResult(matched, changed, null);
        }
        catch (Exception ex)
        {
            if (!muted && restoreOriginalState)
            {
                _originalMuteStates.Clear();
            }

            return new DiscordCableSessionIsolationResult(matched, changed, ex.Message);
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

        lock (_sync)
        {
            if (_enabled)
            {
                try
                {
                    _enabled = false;
                    ApplyMute(muted: false, restoreOriginalState: true);
                }
                catch
                {
                    // Best-effort restore during application shutdown.
                }
            }
        }

        _enumerator.Dispose();
        _disposed = true;
    }
}
