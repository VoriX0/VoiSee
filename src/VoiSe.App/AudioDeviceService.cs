using NAudio.CoreAudioApi;
using VoiSe.Audio;

namespace VoiSe.App;

/// <summary>Owns endpoint discovery and transfers opened endpoint ownership to the engine.</summary>
internal sealed class AudioDeviceService : IDisposable
{
    private readonly AudioDeviceCatalog _catalog = new();

    public AudioDeviceSnapshot Refresh(AudioDevicePreferences preferences, string? requiredInputId, string? requiredMonitorId)
    {
        // Complete both enumerations before the caller replaces its current UI snapshot.
        var capture = _catalog.ListCaptureDevices();
        var render = _catalog.ListRenderDevices();
        return new(capture, render, AudioDeviceSelectionPolicy.Select(capture, render, preferences, requiredInputId, requiredMonitorId));
    }

    public Gate2UnifiedAudioEngine CreateEngine(AudioDeviceSelection selection, EffectSettings settings, EqualizerSettings equalizer)
    {
        MMDevice? input = null, output = null, monitor = null;
        try
        {
            if (selection.Input is null || !AudioDeviceSelectionPolicy.IsVirtualCable(selection.VirtualOutput))
                throw new InvalidOperationException(UiText.L("Selected_devices_are_no_longer_available_Refre_beb32965"));
            input = _catalog.FindCaptureDevice(selection.Input.Id);
            output = _catalog.FindRenderDevice(selection.VirtualOutput!.Id);
            monitor = selection.Monitor is null ? null : _catalog.FindRenderDevice(selection.Monitor.Id);
            if (input is null || output is null || (selection.Monitor is not null && monitor is null))
                throw new InvalidOperationException(UiText.L("Selected_devices_are_no_longer_available_Refre_beb32965"));
            var engine = new Gate2UnifiedAudioEngine(input, output, monitor, settings, ownsDevices: true, equalizer: equalizer);
            input = output = monitor = null; // Ownership transfers only after successful construction.
            return engine;
        }
        finally
        {
            Release(input); Release(output); Release(monitor);
        }
    }

    private static void Release(MMDevice? device)
    {
        try { device?.Dispose(); }
        catch (Exception error) { StartupLog.Write("Endpoint cleanup: " + error); }
    }

    public void Dispose() => _catalog.Dispose();
}
