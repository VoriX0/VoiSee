using Microsoft.UI.Xaml;

namespace VoiSe.App;

public sealed partial class MainWindow
{
    private bool _cleanupLoading = true;
    private void InitializeMicrophoneCleanup()
    {
        // Keep echo enabled when either of the two legacy options was selected.
        EchoCancellationSwitch.IsChecked = _settings.EchoCancellationEnabled || _settings.RoomEchoReductionEnabled;
        _cleanupLoading = false;
    }
    private void OnEchoCleanupChanged(object sender, RoutedEventArgs e) => ApplyMicrophoneCleanupChange();
    private void ApplyMicrophoneCleanupChange()
    {
        if (_loadingSettings || _cleanupLoading) return;
        SaveCurrentSettings(); UpdateMicrophoneCleanupStatus();
        if (_engine is not null) ScheduleEngineRestart("microphone echo cleanup changed");
    }
    private void UpdateMicrophoneCleanupStatus()
    {
        if (EchoCancellationSwitch.IsChecked != true)
        { EchoCleanupStatus.Text = "Эхоподавление выключено."; return; }
        if (_engine is null)
        { EchoCleanupStatus.Text = "Включится после запуска микрофона."; return; }
        EchoCleanupStatus.Text = _engine.EchoCancellationAvailable
            ? "Эхоподавление включено."
            : "Эхо комнаты ослабляется. Звук колонок: " + (_engine.EchoCancellationError ?? "ожидание запуска.");
    }
}
