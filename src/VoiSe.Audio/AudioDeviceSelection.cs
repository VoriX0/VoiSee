namespace VoiSe.Audio;

public sealed record AudioDevicePreference(string? Id, string? Name);
public sealed record AudioDevicePreferences(AudioDevicePreference Input, AudioDevicePreference VirtualOutput, AudioDevicePreference Monitor);
public sealed record AudioDeviceSelection(AudioDeviceInfo? Input, AudioDeviceInfo? VirtualOutput, AudioDeviceInfo? Monitor);
public sealed record AudioDeviceSnapshot(IReadOnlyList<AudioDeviceInfo> Capture, IReadOnlyList<AudioDeviceInfo> Render, AudioDeviceSelection Selection);

/// <summary>Selection rules independent of COM objects, settings files and UI controls.</summary>
public static class AudioDeviceSelectionPolicy
{
    public static bool IsVirtualCable(AudioDeviceInfo? device)
    {
        var name = device?.FriendlyName;
        return !string.IsNullOrWhiteSpace(name) &&
            (name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VB-CABLE", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VB Audio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));
    }

    public static AudioDeviceSelection Select(IReadOnlyList<AudioDeviceInfo> capture, IReadOnlyList<AudioDeviceInfo> render,
        AudioDevicePreferences preferences, string? requiredInputId = null, string? requiredMonitorId = null)
    {
        var input = requiredInputId is not null ? ById(capture, requiredInputId)
            : Pick(capture, preferences.Input) ?? ByName(capture, "Fifine") ?? capture.FirstOrDefault();
        var virtualOutput = Pick(render, preferences.VirtualOutput) ?? ByName(render, "CABLE Input");
        if (!IsVirtualCable(virtualOutput)) virtualOutput = render.FirstOrDefault(IsVirtualCable);
        var monitor = requiredMonitorId is not null ? ById(render, requiredMonitorId)
            : Pick(render, preferences.Monitor) ?? ByName(render, "Realtek") ?? render.FirstOrDefault();
        return new(input, virtualOutput, monitor);
    }

    private static AudioDeviceInfo? ById(IReadOnlyList<AudioDeviceInfo> devices, string? id) =>
        id is null ? null : devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    private static AudioDeviceInfo? ByName(IReadOnlyList<AudioDeviceInfo> devices, string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : devices.FirstOrDefault(d => d.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase));
    private static AudioDeviceInfo? Pick(IReadOnlyList<AudioDeviceInfo> devices, AudioDevicePreference preference) =>
        ById(devices, preference.Id)
        ?? (string.IsNullOrWhiteSpace(preference.Name) ? null : devices.FirstOrDefault(d => string.Equals(d.FriendlyName, preference.Name, StringComparison.OrdinalIgnoreCase)))
        ?? ByName(devices, preference.Name);
}
