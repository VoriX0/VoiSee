using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace VoiSe.App;

public sealed partial class MainWindow
{
    private EqualizerEditHistory? _equalizerHistory;
    private void InitializeEqualizerUndo()
    {
        _equalizerHistory = new(_settings.Equalizer, _selectedEqBand);
        TrackEqualizerSliderGesture(EqualizerPreamp);
        // Preview runs before NumberBox's internal TextBox consumes Ctrl+Z.
        RootGrid.PreviewKeyDown += OnEqualizerUndoKeyDown;
        UpdateEqualizerUndoButton();
    }
    private void TrackEqualizerSliderGesture(Slider slider)
    {
        slider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _equalizerHistory?.BeginGesture()), true);
        slider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => EndEqualizerGesture()), true);
        slider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => EndEqualizerGesture()), true);
    }
    private void EndEqualizerGesture()
    {
        _equalizerHistory?.EndGesture();
        UpdateEqualizerUndoButton();
    }
    private void UpdateEqualizerUndoButton() => EqualizerUndo.IsEnabled = _equalizerHistory?.Count > 0;
    private void OnEqualizerUndo(object sender, RoutedEventArgs e) => UndoEqualizerEdit();
    private void OnEqualizerUndoKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ReferenceEquals(MainTabView.SelectedItem, EqualizerTab) || _capturingHotkey) return;
        static bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
        if (e.Key != VirtualKey.Z || !Down(VirtualKey.Control) || Down(VirtualKey.Shift) || Down(VirtualKey.Menu)) return;
        UndoEqualizerEdit();
        e.Handled = true;
    }
    private void UndoEqualizerEdit()
    {
        var state = _equalizerHistory?.Undo();
        if (state is null) { UpdateEqualizerUndoButton(); return; }
        _settings.Equalizer = state.Settings;
        _selectedEqBand = state.SelectedBand;
        SyncEqualizerEditors();
        RebuildEqualizerBands();
        // Skip history while applying an undo, but retain normal audio/persistence updates.
        QueueEqualizerChange(recordHistory: false);
        RenderEqualizerCurve();
        UpdateEqualizerUndoButton();
    }
}
