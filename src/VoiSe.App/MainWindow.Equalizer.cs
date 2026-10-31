using System;
using System.Globalization;
using System.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using VoiSe.Audio;

namespace VoiSe.App;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _equalizerApplyTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _equalizerSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _equalizerLoading = true;
    private int _equalizerColumns;
    private static readonly ResourceManager EqualizerResources = new("VoiSe.App.Resources.Equalizer", typeof(MainWindow).Assembly);
    private static string EqText(string key) => EqualizerResources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private void InitializeEqualizer()
    {
        _settings.Equalizer = (_settings.Equalizer ?? new()).NormalizedCopy();
        EqualizerTab.Header = EqText("Title");
        EqualizerTitle.Text = EqText("Title");
        EqualizerDescription.Text = EqText("Description");
        EqualizerEnabled.Header = EqText("Enabled");
        EqualizerAdvanced.Header = EqText("Advanced");
        EqualizerReset.Content = EqText("Reset");
        EqualizerPreampTitle.Text = EqText("Preamp");
        AutomationProperties.SetName(EqualizerPreamp, EqText("Preamp"));
        EqualizerHint.Text = EqText("Hint");
        EqualizerEnabled.IsOn = _settings.Equalizer.Enabled;
        EqualizerAdvanced.IsOn = _settings.EqualizerAdvancedMode;
        EqualizerPreamp.Value = _settings.Equalizer.PreampDb;
        EqualizerPreampValue.Text = Db(EqualizerPreamp.Value);
        _equalizerApplyTimer.Tick += (_, _) =>
        {
            _equalizerApplyTimer.Stop();
            _engine?.UpdateEqualizer(_settings.Equalizer);
        };
        _equalizerSaveTimer.Tick += (_, _) =>
        {
            _equalizerSaveTimer.Stop();
            try { _settingsStore.Save(_settings); }
            catch (Exception ex) { StartupLog.Write("Equalizer settings save failed: " + ex); }
        };
        _equalizerLoading = false;
        RebuildEqualizerBands();
    }

    private static string Db(double value) => value.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " dB";
    private void QueueEqualizerChange(bool audio = true)
    {
        // Throttle audio updates while dragging, debounce slower disk writes.
        if (audio && !_equalizerApplyTimer.IsEnabled) _equalizerApplyTimer.Start();
        _equalizerSaveTimer.Stop(); _equalizerSaveTimer.Start();
    }
    private void OnEqualizerEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_equalizerLoading) return;
        _settings.Equalizer.Enabled = EqualizerEnabled.IsOn;
        QueueEqualizerChange();
    }
    private void OnEqualizerModeChanged(object sender, RoutedEventArgs e)
    {
        if (_equalizerLoading) return;
        _settings.EqualizerAdvancedMode = EqualizerAdvanced.IsOn;
        RebuildEqualizerBands();
        QueueEqualizerChange(audio: false);
    }
    private void OnEqualizerPreampChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_equalizerLoading) return;
        _settings.Equalizer.PreampDb = e.NewValue;
        EqualizerPreampValue.Text = Db(e.NewValue);
        QueueEqualizerChange();
    }
    private void OnEqualizerReset(object sender, RoutedEventArgs e)
    {
        _settings.Equalizer = new EqualizerSettings { Enabled = EqualizerEnabled.IsOn };
        EqualizerPreamp.Value = 0;
        RebuildEqualizerBands();
        QueueEqualizerChange();
    }
    private void RebuildEqualizerBands()
    {
        EqualizerBands.Children.Clear();
        var advanced = _settings.EqualizerAdvancedMode;
        var count = advanced ? 31 : 10;
        for (var i = 0; i < count; i++)
        {
            var basicIndex = i;
            var index = advanced ? i : EqualizerSettings.BasicBands[i];
            var frequency = EqualizerSettings.Frequencies[index];
            var label = frequency >= 1000 ? (frequency / 1000).ToString("0.##", CultureInfo.CurrentCulture) + " kHz"
                : frequency.ToString("0.#", CultureInfo.CurrentCulture) + " Hz";
            var value = new TextBlock { Text = Db(_settings.Equalizer.GainsDb[index]), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
            var slider = new Slider { Orientation = Orientation.Vertical, Minimum = -12, Maximum = 12,
                StepFrequency = .5, Value = _settings.Equalizer.GainsDb[index], Height = 180, HorizontalAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(slider, label);
            ToolTipService.SetToolTip(slider, label + " · −12 … +12 dB");
            slider.ValueChanged += (_, e) =>
            {
                if (advanced) _settings.Equalizer.GainsDb[index] = e.NewValue;
                else _settings.Equalizer.SetBasicGain(basicIndex, e.NewValue);
                value.Text = Db(e.NewValue);
                QueueEqualizerChange();
            };
            var panel = new StackPanel { Spacing = 8, Padding = new Thickness(2, 8, 2, 8) };
            panel.Children.Add(new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            panel.Children.Add(slider); panel.Children.Add(value);
            EqualizerBands.Children.Add(panel);
        }
        LayoutEqualizerBands();
    }
    private void OnEqualizerBandsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_equalizerLoading) LayoutEqualizerBands();
    }
    private void LayoutEqualizerBands()
    {
        var count = EqualizerBands.Children.Count;
        if (count == 0) return;
        var columns = Math.Clamp((int)(Math.Max(72, EqualizerBands.ActualWidth) / 76), 1, count);
        // Rebuild definitions only when the layout actually changes.
        if (columns != _equalizerColumns || EqualizerBands.RowDefinitions.Count != (count + columns - 1) / columns)
        {
            _equalizerColumns = columns;
            EqualizerBands.ColumnDefinitions.Clear(); EqualizerBands.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) EqualizerBands.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < (count + columns - 1) / columns; i++) EqualizerBands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var i = 0; i < count; i++) { Grid.SetColumn((FrameworkElement)EqualizerBands.Children[i], i % columns); Grid.SetRow((FrameworkElement)EqualizerBands.Children[i], i / columns); }
    }
}
