using Microsoft.UI.Xaml;
using VoiSe.Audio;

namespace VoiSe.App;

// UI adaptation only; lifecycle and endpoint ownership live in dedicated services.
public sealed partial class MainWindow
{
    private void OnRefreshDevicesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var resume = _audioSession.State == AudioSessionState.Faulted && !_manualStopRequested
                ? AudioResumePlan.Capture(SelectedAudioDevices(), running: true) : null;
            RefreshDevices();
            if (resume is not null && resume.Matches(SelectedAudioDevices()) && _engine is null)
                StartEngine(logAlreadyRunning: false);
        }
        catch (Exception ex)
        {
            StartupLog.Write("RefreshDevices button error: " + ex);
            AppendLog($"Device refresh error: {ex.Message}");
        }
    }

    private void RefreshDevices(bool saveAfterRefresh = true)
    {
        var wasRunning = _engine is not null;
        var oldInput = (InputDeviceComboBox.SelectedItem as AudioDeviceInfo)?.Id;
        var oldMonitor = (MonitorOutputComboBox.SelectedItem as AudioDeviceInfo)?.Id;
        var oldVirtual = (VirtualOutputComboBox.SelectedItem as AudioDeviceInfo)?.Id;
        _refreshingDevices = true;
        try
        {
            AppendLog("Refreshing audio devices...");
            var preferences = new AudioDevicePreferences(
                new(oldInput ?? _settings.InputDeviceId, _settings.InputDeviceName),
                new(oldVirtual ?? _settings.VirtualOutputDeviceId, _settings.VirtualOutputDeviceName),
                new(oldMonitor ?? _settings.MonitorOutputDeviceId, _settings.MonitorOutputDeviceName));
            var snapshot = _devices.Refresh(preferences, saveAfterRefresh ? oldInput : null, saveAfterRefresh ? oldMonitor : null);
            ApplyDeviceLists(snapshot);
            if (saveAfterRefresh && !_loadingSettings) SaveCurrentSettings();
            if (wasRunning && (oldInput != (InputDeviceComboBox.SelectedItem as AudioDeviceInfo)?.Id
                || oldMonitor != (MonitorOutputComboBox.SelectedItem as AudioDeviceInfo)?.Id
                || oldVirtual != (VirtualOutputComboBox.SelectedItem as AudioDeviceInfo)?.Id))
            {
                StopEngine(log: true);
                if (InputDeviceComboBox.SelectedItem is not null && VirtualOutputComboBox.SelectedItem is not null)
                    StartEngine(logAlreadyRunning: false);
                else AppendLog("Selected audio device was disconnected. Select an available device to resume.");
            }
        }
        finally
        {
            _refreshingDevices = false;
        }
    }

    private void ApplyDeviceLists(AudioDeviceSnapshot snapshot)
    {
        InputDeviceComboBox.ItemsSource = snapshot.Capture;
        VirtualOutputComboBox.ItemsSource = snapshot.Render;
        MonitorOutputComboBox.ItemsSource = snapshot.Render;
        var selected = snapshot.Selection;
        InputDeviceComboBox.SelectedItem = selected.Input;
        VirtualOutputComboBox.SelectedItem = selected.VirtualOutput;
        MonitorOutputComboBox.SelectedItem = selected.Monitor;
        AppendLog($"Devices refreshed: {snapshot.Capture.Count} capture, {snapshot.Render.Count} render.");
        AppendLog($"Selected input: {selected.Input?.FriendlyName ?? "none"}");
        AppendLog($"Selected virtual output: {selected.VirtualOutput?.FriendlyName ?? "none"}");
        AppendLog($"Selected monitor: {selected.Monitor?.FriendlyName ?? "none"}");
        UpdateVBCableUiState();
    }

}
