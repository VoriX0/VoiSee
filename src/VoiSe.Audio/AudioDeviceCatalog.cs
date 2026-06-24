using NAudio.CoreAudioApi;

namespace VoiSe.Audio;

public sealed class AudioDeviceCatalog : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public IReadOnlyList<AudioDeviceInfo> ListCaptureDevices()
        => ListDevices(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceInfo> ListRenderDevices()
        => ListDevices(DataFlow.Render);

    public MMDevice? FindCaptureDevice(string? query)
        => FindDevice(DataFlow.Capture, query);

    public MMDevice? FindRenderDevice(string? query)
        => FindDevice(DataFlow.Render, query);

    private IReadOnlyList<AudioDeviceInfo> ListDevices(DataFlow flow)
    {
        return _enumerator
            .EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, flow.ToString(), d.State.ToString()))
            .OrderBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private MMDevice? FindDevice(DataFlow flow, string? query)
    {
        var devices = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList();
        if (devices.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return flow == DataFlow.Capture
                ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                : _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        return devices.FirstOrDefault(d => d.ID.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => d.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _enumerator.Dispose();
    }
}
