using NAudio.CoreAudioApi;

namespace VoiSe.Audio;

public sealed class AudioDeviceCatalog : IDisposable
{
    private bool _disposed;
    public IReadOnlyList<AudioDeviceInfo> ListCaptureDevices() => ListDevices(DataFlow.Capture);
    public IReadOnlyList<AudioDeviceInfo> ListRenderDevices() => ListDevices(DataFlow.Render);
    public MMDevice? FindCaptureDevice(string? query) => FindDevice(DataFlow.Capture, query);
    public MMDevice? FindRenderDevice(string? query) => FindDevice(DataFlow.Render, query);
    private IReadOnlyList<AudioDeviceInfo> ListDevices(DataFlow flow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // A fresh COM enumerator/snapshot per refresh; no device objects survive in the UI list.
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioDeviceInfo>();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (endpoint)
                result.Add(new AudioDeviceInfo(endpoint.ID, endpoint.FriendlyName, flow.ToString(), endpoint.State.ToString()));
        }
        return result.OrderBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private MMDevice? FindDevice(DataFlow flow, string? query)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var enumerator = new MMDeviceEnumerator();
        if (string.IsNullOrWhiteSpace(query))
            return enumerator.GetDefaultAudioEndpoint(flow, flow == DataFlow.Capture ? Role.Communications : Role.Multimedia);
        var devices = ListDevices(flow);
        var match = devices.FirstOrDefault(d => d.Id.Equals(query, StringComparison.OrdinalIgnoreCase));
        // Saved endpoint IDs must never silently resolve to another microphone.
        if (match is null && !query.StartsWith("{", StringComparison.Ordinal))
            match = devices.FirstOrDefault(d => d.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : enumerator.GetDevice(match.Id);
    }
    public void Dispose() => _disposed = true;
}
