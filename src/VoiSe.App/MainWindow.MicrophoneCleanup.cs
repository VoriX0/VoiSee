using Microsoft.UI.Xaml;

namespace VoiSe.App;

public sealed partial class MainWindow
{
    private bool _cleanupLoading = true;
    private void InitializeMicrophoneCleanup()
    {
        // Keep echo enabled when either of the two legacy options was selected.
        EchoCancellationSwitch.IsOn = _settings.EchoCancellationEnabled || _settings.RoomEchoReductionEnabled;
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
        if (!EchoCancellationSwitch.IsOn)
        { EchoCleanupStatus.Text = UiText.L("Text_a07466f3"); return; }
        if (_engine is null)
        { EchoCleanupStatus.Text = UiText.L("Text_916d117d"); return; }
        EchoCleanupStatus.Text = _engine.EchoCancellationAvailable
            ? UiText.L("Text_d451055b")
            : UiText.L("Text_c5e6370e") + UiText.ErrorMessage(_engine.EchoCancellationError ?? UiText.L("Text_047f7735"));
    }
}
