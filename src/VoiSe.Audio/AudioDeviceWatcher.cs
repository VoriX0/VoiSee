using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiSe.Audio;

/// <summary>Core Audio notifications only enqueue a refresh; no enumeration or disposal inside callbacks.</summary>
public sealed class AudioDeviceWatcher : IMMNotificationClient, IDisposable
{
    private readonly Action<bool> _changed;
    private MMDeviceEnumerator? _enumerator;
    private volatile bool _disposed;
    public AudioDeviceWatcher(Action<bool> changed, bool subscribe = true)
    {
        _changed = changed;
        if (!subscribe) return;
        var enumerator = new MMDeviceEnumerator();
        try { enumerator.RegisterEndpointNotificationCallback(this); _enumerator = enumerator; }
        catch { enumerator.Dispose(); throw; }
    }
    private void Notify(bool defaultPlaybackChanged = false)
    {
        if (_disposed) return;
        try { _changed(defaultPlaybackChanged); } catch { /* Never unwind into a COM callback. */ }
    }
    public void OnDeviceAdded(string deviceId) => Notify();
    public void OnDeviceRemoved(string deviceId) => Notify();
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Notify();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) Notify(true);
    }
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { /* Volume/property traffic is not a device-list change. */ }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var enumerator = _enumerator; _enumerator = null;
        if (enumerator is null) return;
        try { enumerator.UnregisterEndpointNotificationCallback(this); }
        finally { enumerator.Dispose(); }
    }
}
