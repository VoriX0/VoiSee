using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VoiSe.Audio;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VoiSe.App;

public sealed partial class MainWindow : Window
{
    private readonly AudioDeviceCatalog _catalog = new();
    private readonly DispatcherTimer _routeRestartTimer;
    private readonly SettingsStore _settingsStore = new();
    private readonly VoiSeUserSettings _settings;
    private Gate2UnifiedAudioEngine? _engine;
    private string? _soundFilePath;
    private bool _refreshingDevices;
    private bool _manualStopRequested;
    private string _pendingRestartReason = "settings changed";
    private bool _voiceMonitorEnabled;
    private bool _loadingSettings;

    public MainWindow()
    {
        StartupLog.Write("MainWindow constructor started.");
        _settings = _settingsStore.Load();
        InitializeComponent();
        Closed += OnClosed;

        _routeRestartTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _routeRestartTimer.Tick += OnRouteRestartTimerTick;

        AppendLog("Gate 4.2 UI started.");
        AppendLog($"Settings path: {_settingsStore.SettingsPath}");
        StartupLog.Write("MainWindow initialized; restoring settings next.");

        DispatcherQueue.TryEnqueue(async () => await RestoreSettingsAndDevicesAsync());
    }

    private async Task RestoreSettingsAndDevicesAsync()
    {
        _loadingSettings = true;
        try
        {
            AppendLog("Restoring saved settings...");
            StartupLog.Write("Restore: started.");

            ApplyStoredScalarSettingsToControls();
            UpdateAllLabels();
            AppendLog("Saved scalar settings applied.");
            StartupLog.Write("Restore: scalar settings applied.");

            AppendLog("Loading audio devices...");
            StartupLog.Write("Restore: loading capture devices.");
            var captureDevices = await Task.Run(() =>
            {
                using var catalog = new AudioDeviceCatalog();
                return catalog.ListCaptureDevices();
            });

            StartupLog.Write($"Restore: capture devices loaded: {captureDevices.Count}.");
            StartupLog.Write("Restore: loading render devices.");
            var renderDevices = await Task.Run(() =>
            {
                using var catalog = new AudioDeviceCatalog();
                return catalog.ListRenderDevices();
            });

            StartupLog.Write($"Restore: render devices loaded: {renderDevices.Count}.");
            ApplyDeviceLists(captureDevices, renderDevices, saveAfterRefresh: false);

            UpdateAllLabels();
            AppendLog("Settings restored.");
            StartupLog.Write("Restore: completed.");
        }
        catch (Exception ex)
        {
            StartupLog.Write("Settings restore error: " + ex);
            AppendLog($"Settings restore error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void ApplyStoredScalarSettingsToControls()
    {
        _soundFilePath = string.IsNullOrWhiteSpace(_settings.LastSoundFilePath) ? null : _settings.LastSoundFilePath;
        SoundFileTextBox.Text = _soundFilePath ?? string.Empty;

        VirtualOutputVolumeSlider.Value = Clamp(_settings.VirtualMicMasterVolume, 0, 1.5);
        SoundVirtualVolumeSlider.Value = Clamp(_settings.SoundBoardVirtualMicVolume, 0, 1.5);
        SoundMonitorVolumeSlider.Value = Clamp(_settings.SoundBoardHeadphonesVolume, 0, 1.5);
        SoundVirtualDelaySlider.Value = Clamp(_settings.SoundBoardVirtualMicDelayMs, 0, 300);
        VoiceGainSlider.Value = Clamp(_settings.VoiceGainDb, -24, 12);
        GateThresholdSlider.Value = Clamp(_settings.GateThresholdDb, -70, -20);
        CompressorThresholdSlider.Value = Clamp(_settings.CompressorThresholdDb, -40, 0);
        _voiceMonitorEnabled = _settings.VoiceMonitorEnabled;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value))
        {
            return min;
        }

        return Math.Min(max, Math.Max(min, value));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _manualStopRequested = true;
        SaveCurrentSettings();
        StopEngine(log: false);
        _catalog.Dispose();
    }

    private void OnRefreshDevicesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshDevices();
        }
        catch (Exception ex)
        {
            StartupLog.Write("RefreshDevices button error: " + ex);
            AppendLog($"Device refresh error: {ex.Message}");
        }
    }

    private void RefreshDevices(bool saveAfterRefresh = true)
    {
        _refreshingDevices = true;
        try
        {
            AppendLog("Refreshing audio devices...");
            StartupLog.Write("RefreshDevices: loading capture devices.");
            var captureDevices = _catalog.ListCaptureDevices();
            StartupLog.Write($"RefreshDevices: capture devices loaded: {captureDevices.Count}.");

            StartupLog.Write("RefreshDevices: loading render devices.");
            var renderDevices = _catalog.ListRenderDevices();
            StartupLog.Write($"RefreshDevices: render devices loaded: {renderDevices.Count}.");

            ApplyDeviceLists(captureDevices, renderDevices, saveAfterRefresh);
        }
        finally
        {
            _refreshingDevices = false;
        }
    }

    private void ApplyDeviceLists(IReadOnlyList<AudioDeviceInfo> captureDevices, IReadOnlyList<AudioDeviceInfo> renderDevices, bool saveAfterRefresh)
    {
        var oldInputId = (InputDeviceComboBox.SelectedItem as AudioDeviceInfo)?.Id ?? _settings.InputDeviceId;
        var oldVirtualId = (VirtualOutputComboBox.SelectedItem as AudioDeviceInfo)?.Id ?? _settings.VirtualOutputDeviceId;
        var oldMonitorId = (MonitorOutputComboBox.SelectedItem as AudioDeviceInfo)?.Id ?? _settings.MonitorOutputDeviceId;

        InputDeviceComboBox.ItemsSource = captureDevices;
        VirtualOutputComboBox.ItemsSource = renderDevices;
        MonitorOutputComboBox.ItemsSource = renderDevices;

        var selectedInput = PickById(captureDevices, oldInputId)
            ?? PickByExactName(captureDevices, _settings.InputDeviceName)
            ?? PickByName(captureDevices, _settings.InputDeviceName)
            ?? PickByName(captureDevices, "Fifine")
            ?? captureDevices.FirstOrDefault();

        var selectedVirtual = PickById(renderDevices, oldVirtualId)
            ?? PickByExactName(renderDevices, _settings.VirtualOutputDeviceName)
            ?? PickByName(renderDevices, _settings.VirtualOutputDeviceName)
            ?? PickByName(renderDevices, "CABLE Input")
            ?? renderDevices.FirstOrDefault();

        var selectedMonitor = PickById(renderDevices, oldMonitorId)
            ?? PickByExactName(renderDevices, _settings.MonitorOutputDeviceName)
            ?? PickByName(renderDevices, _settings.MonitorOutputDeviceName)
            ?? PickByName(renderDevices, "Realtek")
            ?? renderDevices.FirstOrDefault();

        InputDeviceComboBox.SelectedItem = selectedInput;
        VirtualOutputComboBox.SelectedItem = selectedVirtual;
        MonitorOutputComboBox.SelectedItem = selectedMonitor;

        AppendLog($"Devices refreshed: {captureDevices.Count} capture, {renderDevices.Count} render.");
        AppendLog($"Selected input: {selectedInput?.FriendlyName ?? "none"}");
        AppendLog($"Selected virtual output: {selectedVirtual?.FriendlyName ?? "none"}");
        AppendLog($"Selected monitor: {selectedMonitor?.FriendlyName ?? "none"}");
        StartupLog.Write($"ApplyDeviceLists: input={selectedInput?.FriendlyName ?? "none"}; virtual={selectedVirtual?.FriendlyName ?? "none"}; monitor={selectedMonitor?.FriendlyName ?? "none"}.");

        if (saveAfterRefresh && !_loadingSettings)
        {
            SaveCurrentSettings();
        }
    }

    private static AudioDeviceInfo? PickByExactName(IReadOnlyList<AudioDeviceInfo> devices, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return devices.FirstOrDefault(d => string.Equals(d.FriendlyName, text, StringComparison.OrdinalIgnoreCase));
    }

    private static AudioDeviceInfo? PickByName(IReadOnlyList<AudioDeviceInfo> devices, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return devices.FirstOrDefault(d => d.FriendlyName.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private static AudioDeviceInfo? PickById(IReadOnlyList<AudioDeviceInfo> devices, string? id)
    {
        return id is null ? null : devices.FirstOrDefault(d => d.Id == id);
    }

    private async void OnBrowseSoundClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".ogg");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        _soundFilePath = file.Path;
        SoundFileTextBox.Text = _soundFilePath;
        SaveCurrentSettings();
        AppendLog($"Sound selected: {_soundFilePath}");
    }

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
            if (logAlreadyRunning)
            {
                AppendLog("Engine is already running.");
            }
            return true;
        }

        var inputInfo = InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        var virtualInfo = VirtualOutputComboBox.SelectedItem as AudioDeviceInfo;
        var monitorInfo = MonitorOutputComboBox.SelectedItem as AudioDeviceInfo;

        if (inputInfo is null || virtualInfo is null)
        {
            AppendLog("Select input microphone and virtual output first.");
            return false;
        }

        var input = _catalog.FindCaptureDevice(inputInfo.Id);
        var virtualOutput = _catalog.FindRenderDevice(virtualInfo.Id);
        var monitor = monitorInfo is null ? null : _catalog.FindRenderDevice(monitorInfo.Id);

        if (input is null || virtualOutput is null)
        {
            AppendLog("Selected devices are not available anymore. Refresh devices and try again.");
            return false;
        }

        try
        {
            SaveCurrentSettings();
            _engine = new Gate2UnifiedAudioEngine(input, virtualOutput, monitor, CreateEffectSettings());
            _engine.Start();
            EngineStatusTextBlock.Text = "Running";
            AppendLog($"Engine started. Input: {input.FriendlyName}");
            AppendLog($"Virtual output: {virtualOutput.FriendlyName}");
            AppendLog($"Monitor: {(monitor is null ? "disabled" : monitor.FriendlyName)}");
            return true;
        }
        catch (Exception ex)
        {
            _engine?.Dispose();
            _engine = null;
            EngineStatusTextBlock.Text = "Error";
            AppendLog($"Engine start error: {ex.Message}");
            return false;
        }
    }

    private void StopEngine(bool log)
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            _engine.Dispose();
            if (log)
            {
                AppendLog("Engine stopped.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Engine stop error: {ex.Message}");
        }
        finally
        {
            _engine = null;
            EngineStatusTextBlock.Text = "Stopped";
        }
    }

    private void RestartEngine(string reason)
    {
        if (_engine is null || _manualStopRequested)
        {
            return;
        }

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
        if (_engine is null || _refreshingDevices)
        {
            return;
        }

        _pendingRestartReason = reason;
        _routeRestartTimer.Stop();
        _routeRestartTimer.Start();
        EngineStatusTextBlock.Text = "Restart pending";
    }

    private void ApplyLiveSettings(string reason)
    {
        UpdateAllLabels();
        SaveCurrentSettings();

        if (_engine is null)
        {
            return;
        }

        _engine.UpdateEffectSettings(CreateEffectSettings());
        AppendLog($"Live settings applied: {reason}.");
    }

    private EffectSettings CreateEffectSettings()
    {
        return new EffectSettings
        {
            VoiceGainDb = (float)VoiceGainSlider.Value,
            GateThresholdDb = (float)GateThresholdSlider.Value,
            CompressorThresholdDb = (float)CompressorThresholdSlider.Value,
            GateEnabled = true,
            CompressorEnabled = true,
            LimiterEnabled = true,
            LimiterCeilingDb = -1.0f,
            VirtualOutputGain = (float)VirtualOutputVolumeSlider.Value,
            VoiceMonitorGain = _voiceMonitorEnabled ? 1.0f : 0.0f
        };
    }

    private void OnPlaySoundClick(object sender, RoutedEventArgs e)
    {
        if (_engine is null)
        {
            AppendLog("Start engine before playing sound.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_soundFilePath) || !File.Exists(_soundFilePath))
        {
            AppendLog("Choose an existing sound file first.");
            return;
        }

        try
        {
            SaveCurrentSettings();
            var delayMs = (int)Math.Round(SoundVirtualDelaySlider.Value);
            var virtualVolume = (float)SoundVirtualVolumeSlider.Value;
            var monitorVolume = (float)SoundMonitorVolumeSlider.Value;
            _engine.PlaySound(_soundFilePath, virtualVolume, monitorVolume, delayMs);
            AppendLog($"Sound started. Virtual delay: {delayMs} ms.");
        }
        catch (Exception ex)
        {
            AppendLog($"Sound playback error: {ex.Message}");
        }
    }

    private void OnStopSoundClick(object sender, RoutedEventArgs e)
    {
        if (_engine is null)
        {
            return;
        }

        _engine.StopSound();
        AppendLog("Sound stopped.");
    }

    private void OnRouteSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingDevices || _loadingSettings)
        {
            return;
        }

        SaveCurrentSettings();
        ScheduleEngineRestart("audio route changed");
    }

    private void OnDelayChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateDelayLabel();
        if (!_loadingSettings)
        {
            SaveCurrentSettings();
        }
    }

    private void OnSoundVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateSoundVolumeLabels();
        if (_loadingSettings)
        {
            return;
        }

        SaveCurrentSettings();
        if (_engine is not null)
        {
            _engine.UpdateSoundVolumes((float)SoundVirtualVolumeSlider.Value, (float)SoundMonitorVolumeSlider.Value);
            AppendLog("SoundBoard route volumes applied live.");
        }
    }

    private void OnOutputVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateOutputVolumeLabels();
        if (!_loadingSettings)
        {
            ApplyLiveSettings("master output volume changed");
        }
    }

    private void OnVoiceSettingsChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateVoiceSettingLabels();
        if (!_loadingSettings)
        {
            ApplyLiveSettings("voice setting changed");
        }
    }

    private void OnToggleVoiceMonitorClick(object sender, RoutedEventArgs e)
    {
        _voiceMonitorEnabled = !_voiceMonitorEnabled;
        UpdateVoiceMonitorButton();
        ApplyLiveSettings(_voiceMonitorEnabled ? "voice monitor enabled" : "voice monitor disabled");
    }

    private void UpdateVoiceMonitorButton()
    {
        if (VoiceMonitorButton is null)
        {
            return;
        }

        VoiceMonitorButton.Content = _voiceMonitorEnabled ? "Voice Monitor: On" : "Voice Monitor: Off";
    }

    private void UpdateAllLabels()
    {
        UpdateDelayLabel();
        UpdateSoundVolumeLabels();
        UpdateOutputVolumeLabels();
        UpdateVoiceSettingLabels();
        UpdateVoiceMonitorButton();
    }

    private void UpdateDelayLabel()
    {
        if (DelayLabel is null || SoundVirtualDelaySlider is null)
        {
            return;
        }

        DelayLabel.Text = $"SoundBoard Virtual Mic Delay: {(int)Math.Round(SoundVirtualDelaySlider.Value)} ms";
    }

    private void UpdateSoundVolumeLabels()
    {
        if (SoundVirtualVolumeLabel is null || SoundMonitorVolumeLabel is null)
        {
            return;
        }

        SoundVirtualVolumeLabel.Text = $"SoundBoard → Virtual Mic: {(int)Math.Round(SoundVirtualVolumeSlider.Value * 100)}%";
        SoundMonitorVolumeLabel.Text = $"SoundBoard → Headphones: {(int)Math.Round(SoundMonitorVolumeSlider.Value * 100)}%";
    }

    private void UpdateOutputVolumeLabels()
    {
        if (VirtualOutputVolumeLabel is null || VirtualOutputVolumeSlider is null)
        {
            return;
        }

        VirtualOutputVolumeLabel.Text = $"Virtual Mic Master: {(int)Math.Round(VirtualOutputVolumeSlider.Value * 100)}%";
    }

    private void UpdateVoiceSettingLabels()
    {
        if (VoiceGainLabel is null || GateThresholdLabel is null || CompressorThresholdLabel is null)
        {
            return;
        }

        VoiceGainLabel.Text = $"Voice Gain: {(int)Math.Round(VoiceGainSlider.Value)} dB";
        GateThresholdLabel.Text = $"Gate Threshold: {(int)Math.Round(GateThresholdSlider.Value)} dB";
        CompressorThresholdLabel.Text = $"Compressor Threshold: {(int)Math.Round(CompressorThresholdSlider.Value)} dB";
    }

    private void SaveCurrentSettings()
    {
        if (_loadingSettings)
        {
            return;
        }

        var input = InputDeviceComboBox?.SelectedItem as AudioDeviceInfo;
        var virtualOutput = VirtualOutputComboBox?.SelectedItem as AudioDeviceInfo;
        var monitor = MonitorOutputComboBox?.SelectedItem as AudioDeviceInfo;

        _settings.InputDeviceId = input?.Id;
        _settings.InputDeviceName = input?.FriendlyName;
        _settings.VirtualOutputDeviceId = virtualOutput?.Id;
        _settings.VirtualOutputDeviceName = virtualOutput?.FriendlyName;
        _settings.MonitorOutputDeviceId = monitor?.Id;
        _settings.MonitorOutputDeviceName = monitor?.FriendlyName;
        _settings.LastSoundFilePath = _soundFilePath;

        _settings.VirtualMicMasterVolume = VirtualOutputVolumeSlider?.Value ?? 1.0;
        _settings.VoiceMonitorEnabled = _voiceMonitorEnabled;
        _settings.SoundBoardVirtualMicVolume = SoundVirtualVolumeSlider?.Value ?? 1.0;
        _settings.SoundBoardHeadphonesVolume = SoundMonitorVolumeSlider?.Value ?? 1.0;
        _settings.SoundBoardVirtualMicDelayMs = SoundVirtualDelaySlider?.Value ?? 85.0;
        _settings.VoiceGainDb = VoiceGainSlider?.Value ?? 0.0;
        _settings.GateThresholdDb = GateThresholdSlider?.Value ?? -45.0;
        _settings.CompressorThresholdDb = CompressorThresholdSlider?.Value ?? -18.0;

        _settingsStore.Save(_settings);
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogTextBox.Text = string.IsNullOrEmpty(LogTextBox.Text)
            ? line
            : LogTextBox.Text + Environment.NewLine + line;
    }
}
