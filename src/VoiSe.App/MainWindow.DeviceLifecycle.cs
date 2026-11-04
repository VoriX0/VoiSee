using Microsoft.UI.Xaml;
using VoiSe.Audio;

namespace VoiSe.App;

public sealed partial class MainWindow
{
    private AudioDeviceWatcher? _deviceWatcher;
    private readonly DispatcherTimer _deviceRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _defaultPlaybackChanged, _audioSuspended;
    private AudioResumePlan? _audioResumePlan;
    private AudioDeviceSelection SelectedAudioDevices() => new(InputDeviceComboBox.SelectedItem as AudioDeviceInfo,
        VirtualOutputComboBox.SelectedItem as AudioDeviceInfo, MonitorOutputComboBox.SelectedItem as AudioDeviceInfo);
    private void InitializeDeviceLifecycle()
    {
        _deviceRefreshTimer.Tick += OnDevicesChanged;
        try
        {
            _deviceWatcher = new(changed => DispatcherQueue.TryEnqueue(() =>
            {
                if (_exitRequested || !_loadedOnce) return;
                _defaultPlaybackChanged |= changed;
                _deviceRefreshTimer.Stop(); _deviceRefreshTimer.Start();
            }));
        }
        catch (Exception error) { StartupLog.Write("Device notifications unavailable; manual Refresh remains available: " + error); }
    }
    private void OnDevicesChanged(object? sender, object args)
    {
        _deviceRefreshTimer.Stop();
        if (_exitRequested || _audioSuspended) return;
        var restartEcho = _defaultPlaybackChanged && _engine is not null && EchoCancellationSwitch.IsOn;
        _defaultPlaybackChanged = false;
        try
        {
            RefreshDevices();
            if (_audioResumePlan is { } resume)
            {
                _audioResumePlan = null;
                if (!_manualStopRequested && resume.Matches(SelectedAudioDevices())) StartEngine(logAlreadyRunning: false);
            }
            else if (restartEcho) RestartEngine("Windows playback device changed");
        }
        catch (Exception error) { AppendLog("Device refresh error: " + error.Message); }
    }
    private void OnPowerModeChanged(bool suspended)
    {
        // The native callback only posts a command; it never waits for capture shutdown.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_exitRequested) return;
            if (suspended)
            {
                _audioSuspended = true;
                _audioResumePlan = AudioResumePlan.Capture(SelectedAudioDevices(), _engine is not null && !_manualStopRequested);
                StopEngine(log: false);
            }
            else
            {
                _audioSuspended = false;
                _deviceRefreshTimer.Stop(); _deviceRefreshTimer.Start();
            }
        });
    }
    private void DisposeDeviceLifecycle()
    {
        _deviceRefreshTimer.Stop();
        try { _deviceWatcher?.Dispose(); } catch (Exception error) { StartupLog.Write("Device watcher cleanup: " + error); }
        _deviceWatcher = null; _audioResumePlan = null;
    }
}
