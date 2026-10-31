using System;
using System.Globalization;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VoiSe.Audio;
using Windows.Foundation;
using Windows.System;

namespace VoiSe.App;

public sealed partial class MainWindow
{
    private int _selectedEqBand = 2;
    private uint? _eqDragPointer;
    private readonly Button[] _eqNodes = new Button[5];
    private readonly Windows.UI.Color[] _eqColors = { ColorHelper.FromArgb(255, 160, 102, 255), ColorHelper.FromArgb(255, 86, 127, 255),
        ColorHelper.FromArgb(255, 255, 84, 170), ColorHelper.FromArgb(255, 255, 100, 104), ColorHelper.FromArgb(255, 255, 161, 85) };
    private double EqWidth => Math.Max(1, EqualizerPlot.ActualWidth - 76);
    private double EqHeight => Math.Max(1, EqualizerPlot.ActualHeight - 64);
    private double EqX(double f) => 52 + Math.Log10(f / 20) / 3 * EqWidth;
    private double EqY(double db) => 24 + (12 - Math.Clamp(db, -12, 12)) / 24 * EqHeight;

    private void InitializeParametricEqualizer()
    {
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var node = new Button { Content = "", Width = 16, Height = 16, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(_eqColors[i]), BorderThickness = new Thickness(2) };
            AutomationProperties.SetName(node, $"Полоса {i + 1}. Стрелки: частота и усиление; колесо: Q");
            node.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
            {
                if (!e.GetCurrentPoint(EqualizerPlot).Properties.IsLeftButtonPressed) return;
                SelectEqualizerBand(index);
                _eqDragPointer = null;
                if (EqualizerPlot.CapturePointer(e.Pointer)) _eqDragPointer = e.Pointer.PointerId;
                e.Handled = true;
            }), true);
            node.PointerWheelChanged += (_, e) =>
            {
                SelectEqualizerBand(index);
                var band = _settings.Equalizer.ParametricBands[index];
                band.Q = Math.Clamp(band.Q * Math.Pow(1.12, e.GetCurrentPoint(EqualizerPlot).Properties.MouseWheelDelta / 120.0), .2, 12);
                ParametricBandEdited(); e.Handled = true;
            };
            node.KeyDown += (_, e) =>
            {
                var band = _settings.Equalizer.ParametricBands[index];
                switch (e.Key)
                {
                    case VirtualKey.Left: band.Frequency = Math.Max(20, band.Frequency / Math.Pow(2, 1.0 / 12)); break;
                    case VirtualKey.Right: band.Frequency = Math.Min(20000, band.Frequency * Math.Pow(2, 1.0 / 12)); break;
                    case VirtualKey.Up: band.GainDb = Math.Min(12, band.GainDb + .5); break;
                    case VirtualKey.Down: band.GainDb = Math.Max(-12, band.GainDb - .5); break;
                    default: return;
                }
                SelectEqualizerBand(index); ParametricBandEdited(); e.Handled = true;
            };
            node.Click += (_, _) => SelectEqualizerBand(index);
            node.DoubleTapped += (_, e) => { _settings.Equalizer.ParametricBands[index].GainDb = 0; SelectEqualizerBand(index); ParametricBandEdited(); e.Handled = true; };
            _eqNodes[i] = node; EqualizerPlot.Children.Add(node);
        }
        SelectEqualizerBand(_selectedEqBand);
    }
    private void SelectEqualizerBand(int index, bool render = true)
    {
        _selectedEqBand = Math.Clamp(index, 0, 4);
        var band = _settings.Equalizer.ParametricBands[_selectedEqBand];
        var loading = _equalizerLoading; _equalizerLoading = true;
        EqualizerBandSelector.SelectedIndex = _selectedEqBand;
        EqualizerBandEnabled.IsOn = band.Enabled;
        EqualizerBandFrequency.Value = band.Frequency;
        EqualizerBandGain.Value = band.GainDb;
        EqualizerBandQ.Value = band.Q;
        EqualizerBandType.SelectedIndex = (int)band.Filter;
        EqualizerBandGain.IsEnabled = band.Filter is not (EqualizerFilter.HighPass or EqualizerFilter.LowPass);
        _equalizerLoading = loading;
        if (render) RenderEqualizerCurve();
    }
    private void ParametricBandEdited()
    {
        SelectEqualizerBand(_selectedEqBand, render: false);
        var band = _settings.Equalizer.ParametricBands[_selectedEqBand];
        var node = _eqNodes[_selectedEqBand];
        Canvas.SetLeft(node, EqX(band.Frequency) - 8);
        Canvas.SetTop(node, EqY(band.Filter is EqualizerFilter.HighPass or EqualizerFilter.LowPass ? 0 : band.GainDb) - 8);
        QueueEqualizerChange();
    }
    private void OnEqualizerBandSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_equalizerLoading && EqualizerBandSelector.SelectedIndex >= 0) SelectEqualizerBand(EqualizerBandSelector.SelectedIndex);
    }
    private void OnEqualizerBandValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_equalizerLoading || !double.IsFinite(e.NewValue)) return;
        var band = _settings.Equalizer.ParametricBands[_selectedEqBand];
        if (sender == EqualizerBandFrequency) band.Frequency = Math.Clamp(e.NewValue, 20, 20000);
        else if (sender == EqualizerBandGain) band.GainDb = Math.Clamp(e.NewValue, -12, 12);
        else band.Q = Math.Clamp(e.NewValue, .2, 12);
        ParametricBandEdited();
    }
    private void OnEqualizerBandEnabled(object sender, RoutedEventArgs e)
    {
        if (_equalizerLoading) return;
        _settings.Equalizer.ParametricBands[_selectedEqBand].Enabled = EqualizerBandEnabled.IsOn;
        ParametricBandEdited();
    }
    private void OnEqualizerBandTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_equalizerLoading || EqualizerBandType.SelectedIndex < 0) return;
        _settings.Equalizer.ParametricBands[_selectedEqBand].Filter = (EqualizerFilter)EqualizerBandType.SelectedIndex;
        ParametricBandEdited();
    }
    private void OnEqualizerPlotMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_eqDragPointer != e.Pointer.PointerId) return;
        var position = e.GetCurrentPoint(EqualizerPlot).Position;
        var band = _settings.Equalizer.ParametricBands[_selectedEqBand];
        band.Frequency = Math.Round(20 * Math.Pow(1000, Math.Clamp((position.X - 52) / EqWidth, 0, 1)), 1);
        if (band.Filter is not (EqualizerFilter.HighPass or EqualizerFilter.LowPass))
            band.GainDb = Math.Round(Math.Clamp(12 - (position.Y - 24) / EqHeight * 24, -12, 12), 1);
        ParametricBandEdited(); e.Handled = true;
    }
    private void OnEqualizerPlotReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_eqDragPointer != e.Pointer.PointerId) return;
        _eqDragPointer = null;
        EqualizerPlot.ReleasePointerCapture(e.Pointer);
    }
    private void OnEqualizerPlotCaptureLost(object sender, PointerRoutedEventArgs e) => _eqDragPointer = null;
    private void OnEqualizerPlotSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_equalizerLoading) return;
        DrawEqualizerGrid(); RenderEqualizerCurve();
    }
    private void DrawEqualizerGrid()
    {
        EqualizerPlotGrid.Children.Clear();
        var brush = Application.Current.Resources["VoiSee.SecondaryTextBrush"] as Brush;
        foreach (var db in new[] { -12, -6, 0, 6, 12 })
        {
            EqualizerPlotGrid.Children.Add(new Line { X1 = 52, X2 = 52 + EqWidth, Y1 = EqY(db), Y2 = EqY(db), Stroke = brush, Opacity = db == 0 ? .6 : .15 });
            var text = new TextBlock { Text = db + " dB", FontSize = 11, Foreground = brush };
            Canvas.SetLeft(text, 2); Canvas.SetTop(text, EqY(db) - 8); EqualizerPlotGrid.Children.Add(text);
        }
        foreach (var frequency in new[] { 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
        {
            var x = EqX(frequency);
            EqualizerPlotGrid.Children.Add(new Line { X1 = x, X2 = x, Y1 = 24, Y2 = 24 + EqHeight, Stroke = brush, Opacity = .15 });
            var text = new TextBlock { Text = frequency >= 1000 ? frequency / 1000 + "k" : frequency.ToString(), FontSize = 11, Foreground = brush };
            Canvas.SetLeft(text, x - 12); Canvas.SetTop(text, 30 + EqHeight); EqualizerPlotGrid.Children.Add(text);
        }
    }
    private void RenderParametricCurve()
    {
        if (EqualizerPlot == null) return;
        var filters = _settings.Equalizer.GetResponseFilters();
        var points = new PointCollection();
        var selectedPoints = new PointCollection();
        var selected = _settings.Equalizer.ParametricBands[_selectedEqBand];
        var selectedFilter = EqualizerCoefficients.Create(selected.Frequency, selected.GainDb, selected.Q, 48000, selected.Filter);
        var fill = new PointCollection { new Point(EqX(20), EqY(0)) };
        var maxDb = -120.0;
        for (var i = 0; i <= 256; i++)
        {
            var f = 20 * Math.Pow(1000, i / 256.0);
            var db = _settings.Equalizer.PreampDb;
            foreach (var filter in filters) db += filter.ResponseDb(f);
            maxDb = Math.Max(maxDb, db);
            var point = new Point(EqX(f), EqY(db)); points.Add(point); fill.Add(point);
            selectedPoints.Add(new Point(EqX(f), EqY(selected.Enabled ? selectedFilter.ResponseDb(f) : 0)));
        }
        fill.Add(new Point(EqX(20000), EqY(0)));
        EqualizerCurve.Points = points; EqualizerCurveFill.Points = fill; EqualizerSelectedCurve.Points = selectedPoints;
        var color = new SolidColorBrush(_eqColors[_selectedEqBand]);
        EqualizerSelectedCurve.Stroke = color; EqualizerCurveFill.Fill = color;
        EqualizerHeadroom.Text = maxDb > 0.5 ? $"Пик коррекции +{maxDb:F1} dB · при перегрузе уменьшите предусиление" : "Есть запас по усилению";
        for (var i = 0; i < 5; i++)
        {
            if (_eqNodes[i] == null) continue;
            var band = _settings.Equalizer.ParametricBands[i]; var node = _eqNodes[i];
            Canvas.SetLeft(node, EqX(band.Frequency) - 8);
            Canvas.SetTop(node, EqY(band.Filter is EqualizerFilter.HighPass or EqualizerFilter.LowPass ? 0 : band.GainDb) - 8);
            node.Opacity = band.Enabled ? 1 : .35;
            node.BorderBrush = i == _selectedEqBand ? Application.Current.Resources["VoiSee.PrimaryTextBrush"] as Brush : new SolidColorBrush(_eqColors[i]);
            ToolTipService.SetToolTip(node, $"{i + 1}: {band.Frequency:F0} Hz · {band.GainDb:+0.0;-0.0;0} dB · Q {band.Q:F2}");
        }
    }
}
