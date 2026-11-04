using Microsoft.UI.Xaml;

namespace VoiSe.App;

public sealed partial class MainWindow
{
    private readonly PersistenceCoordinator _persistence = new();
    private readonly DispatcherTimer _settingsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _settingsSavePending;
    private void ScheduleSettingsSave()
    {
        _settingsSavePending = true;
        _settingsSaveTimer.Stop(); _settingsSaveTimer.Start();
    }
    private void OnSettingsSaveTimerTick(object? sender, object args) => FlushPendingSettings();
    private void FlushPendingSettings()
    {
        _settingsSaveTimer.Stop();
        if (!_settingsSavePending) return;
        _settingsSavePending = false;
        _persistence.Save("settings", () => _settingsStore.Save(_settings));
    }
    private void OnRetrySaveClick(object sender, RoutedEventArgs e) => _persistence.Retry();
    private void UpdatePersistenceStatus()
    {
        if (_exitRequested || StorageErrorInfoBar is null) return;
        StorageErrorInfoBar.IsOpen = _persistence.PendingCount > 0;
        StorageErrorInfoBar.Message = _persistence.LastError is null ? string.Empty : UiText.ErrorMessage(_persistence.LastError);
    }
}
