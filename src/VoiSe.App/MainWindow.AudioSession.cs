using Microsoft.UI.Xaml;
using VoiSe.Audio;

namespace VoiSe.App;

// UI adaptation only; lifecycle and endpoint ownership live in dedicated services.
public sealed partial class MainWindow
{
    private void OnStartEngineClick(object sender, RoutedEventArgs e)
    {
        _manualStopRequested = false;
        StartEngine(logAlreadyRunning: true);
    }

    private void OnStopEngineClick(object sender, RoutedEventArgs e)
    {
        _manualStopRequested = true;
        StopEngine(log: true);
    }

    private bool StartEngine(bool logAlreadyRunning)
    {
        if (_engine is not null)
        {
            if (logAlreadyRunning) AppendLog("Engine is already running.");
            return true;
        }

        var inputInfo = InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        var virtualInfo = VirtualOutputComboBox.SelectedItem as AudioDeviceInfo;
        var monitorInfo = MonitorOutputComboBox.SelectedItem as AudioDeviceInfo;

        if (inputInfo is null)
        {
            AppendLog("Select an input microphone first.");
            UpdateVBCableUiState();
            return false;
        }

        if (virtualInfo is null || !IsLikelyVBCableDevice(virtualInfo))
        {
            AppendLog("VB-CABLE was not detected. Install VB-CABLE before starting the engine.");
            UpdateVBCableUiState();
            return false;
        }

        try
        {
            SaveCurrentSettings();
            var selection = new AudioDeviceSelection(inputInfo, virtualInfo, monitorInfo);
            _audioSession.Start(() => _devices.CreateEngine(selection, CreateEffectSettings(), CurrentEqualizerForEngine()),
                engine =>
                {
                    engine.SetVirtualMicMuted(_virtualMicMuted);
                    engine.UpdateMediaBridgeVolume((float)(MediaBridgeVolumeSlider?.Value ?? _settings.MediaBridgeVirtualMicVolume));
                });
            var engine = _engine!; // Start either publishes a running session or throws.
            EngineStatusTextBlock.Text = UiText.L("Running_f4ccae29");
            AppendLog($"Engine started. Input: {inputInfo.FriendlyName}");
            AppendLog($"Virtual output: {virtualInfo.FriendlyName}");
            AppendLog($"Monitor: {(monitorInfo is null ? "disabled" : monitorInfo.FriendlyName)}");
            AppendLog($"Voice monitor route: {(engine.VoiceMonitorRouteEnabled ? "connected" : "hard disconnected")}");
            var noiseMode = GetSelectedNoiseSuppressionMode();
            if (noiseMode != NoiseSuppressionMode.Off)
            {
                AppendLog(engine.NoiseSuppressionAvailable
                    ? $"Noise suppression: {engine.ActiveNoiseSuppressionName} active at {(int)Math.Round(NoiseSuppressionStrengthSlider?.Value ?? 70.0)}%."
                    : $"Noise suppression unavailable: {engine.NoiseSuppressionError ?? $"{engine.ActiveNoiseSuppressionName} could not be initialized"}.");
            }
            // On-demand playback: never decode the whole library on engine start.
            RefreshAdvancedSettingsStatus();
            return true;
        }
        catch (Exception ex)
        {
            EngineStatusTextBlock.Text = UiText.L("Error_54a0e8c1");
            AppendLog($"Engine start error: {ex.Message}");
            RefreshAdvancedSettingsStatus();
            return false;
        }
    }

    private void StopEngine(bool log)
    {
        _routeRestartTimer.Stop();
        var wasRunning = _engine is not null;
        _audioSession.Stop();
        if (!wasRunning) return;
        if (log) AppendLog("Engine stopped.");
        UpdateStoppedEngineUi();
    }

    private void OnAudioSessionFaulted(string error)
    {
        if (_exitRequested) return;
        _routeRestartTimer.Stop();
        AppendLog("Audio engine error: " + error);
        UpdateStoppedEngineUi();
        EngineStatusTextBlock.Text = UiText.L("Error_refresh_devices_to_resume_513bcc20");
    }

    private void UpdateStoppedEngineUi()
    {
        _mediaBridgeStartedAt = null;
        EngineStatusTextBlock.Text = UiText.L("Stopped_1a4f630a");
        UpdateVBCableUiState();
        RefreshAdvancedSettingsStatus();
        UpdateMediaBridgeUiState(UiText.L("Audio_engine_stopped_c5ac8401"));
    }

    private void RestartEngine(string reason)
    {
        if (_engine is null || _manualStopRequested) return;
        AppendLog($"Engine auto-restart: {reason}.");
        StopEngine(log: false);
        StartEngine(logAlreadyRunning: false);
    }

    private void OnRouteRestartTimerTick(object? sender, object e)
    {
        _routeRestartTimer.Stop();
        RestartEngine(_pendingRestartReason);
    }

    private void ScheduleEngineRestart(string reason)
    {
        if (_engine is null || _refreshingDevices || _loadingSettings) return;
        _pendingRestartReason = reason;
        _routeRestartTimer.Stop();
        _routeRestartTimer.Start();
        EngineStatusTextBlock.Text = UiText.L("Restart_pending_762d0599");
        RefreshAdvancedSettingsStatus();
    }

}
