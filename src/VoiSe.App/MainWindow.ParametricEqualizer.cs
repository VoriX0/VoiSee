using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
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
    private readonly List<Thumb> _eqNodes = new();
    private readonly Line _eqWidthLine = new() { StrokeThickness = 2, IsHitTestVisible = false };
    private Thumb _eqWidthLeft = null!, _eqWidthRight = null!;
    private ControlTemplate _eqPointTemplate = null!;
    private Windows.UI.Color EqColor(int i) => _eqColors[i % _eqColors.Length];
    private readonly Windows.UI.Color[] _eqColors = { ColorHelper.FromArgb(255, 160, 102, 255), ColorHelper.FromArgb(255, 86, 127, 255),
        ColorHelper.FromArgb(255, 255, 84, 170), ColorHelper.FromArgb(255, 255, 100, 104), ColorHelper.FromArgb(255, 255, 161, 85) };
    private double EqWidth => Math.Max(1, EqualizerPlot.ActualWidth - 76);
    private double EqHeight => Math.Max(1, EqualizerPlot.ActualHeight - 64);
    private double EqX(double f) => 52 + Math.Log10(f / 20) / 3 * EqWidth;
    private double EqY(double db) => 24 + (12 - Math.Clamp(db, -12, 12)) / 24 * EqHeight;

    private void InitializeParametricEqualizer()
    {
        _eqPointTemplate = (ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Grid Background='Transparent'><Ellipse Width='16' Height='16' Fill='{TemplateBinding Background}' Stroke='{TemplateBinding BorderBrush}' StrokeThickness='2'/></Grid></ControlTemplate>");
        EqualizerPlot.Children.Add(_eqWidthLine);
        _eqWidthLeft = CreateWidthHandle(); _eqWidthRight = CreateWidthHandle();
        EqualizerPlot.Children.Add(_eqWidthLeft); EqualizerPlot.Children.Add(_eqWidthRight);
        RebuildParametricPoints();
    }
    private Thumb CreateWidthHandle()
    {
        var handle = new Thumb { Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Template = _eqPointTemplate, ManipulationMode = ManipulationModes.None };
        AutomationProperties.SetName(handle, "Ширина настройки: тяните в сторону от точки, чтобы охватить больше частот");
        handle.DragDelta += (_, e) =>
        {
            ChangeEqualizerWidth(Canvas.GetLeft(handle) + 12 + e.HorizontalChange);
        };
        return handle;
    }
    private void RebuildParametricPoints()
    {
        foreach (var node in _eqNodes) EqualizerPlot.Children.Remove(node);
        _eqNodes.Clear();
        var loading = _equalizerLoading; _equalizerLoading = true;
        EqualizerBandSelector.Items.Clear();
        for (var i = 0; i < _settings.Equalizer.ParametricBands.Length; i++)
        {
            var index = i;
            EqualizerBandSelector.Items.Add(new ComboBoxItem { Content = $"Точка {i + 1}" });
            var node = new Thumb { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Template = _eqPointTemplate,
                Background = new SolidColorBrush(EqColor(i)), ManipulationMode = ManipulationModes.None, IsTabStop = true };
            AutomationProperties.SetName(node, $"Точка {i + 1}. Стрелки: частота и усиление. Колесо: ширина");
            node.DragStarted += (_, _) => SelectEqualizerBand(index);
            node.DragDelta += (_, e) =>
            {
                MoveEqualizerPoint(index, Canvas.GetLeft(node) + 14 + e.HorizontalChange, Canvas.GetTop(node) + 14 + e.VerticalChange);
            };
            node.PointerWheelChanged += (_, e) =>
            {
                SelectEqualizerBand(index);
                var band = _settings.Equalizer.ParametricBands[index];
                band.Q = Math.Clamp(band.Q * Math.Pow(1.12, -e.GetCurrentPoint(EqualizerPlot).Properties.MouseWheelDelta / 120.0), .2, 12);
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
            node.GotFocus += (_, _) => SelectEqualizerBand(index);
            node.DoubleTapped += (_, e) => { _settings.Equalizer.ParametricBands[index].GainDb = 0; SelectEqualizerBand(index); ParametricBandEdited(); e.Handled = true; };
            _eqNodes.Add(node); EqualizerPlot.Children.Add(node);
        }
        _equalizerLoading = loading;
        SelectEqualizerBand(_selectedEqBand);
    }
    private void OnAddEqualizerPoint(object sender, RoutedEventArgs e)
    {
        var positions = _settings.Equalizer.ParametricBands.Select(b => b.Frequency).Append(20).Append(20000).OrderBy(f => f).ToArray();
        var gap = positions.Zip(positions.Skip(1), (left, right) => (left, right)).OrderByDescending(p => p.right / p.left).First();
        AddEqualizerPointAt(Math.Sqrt(gap.left * gap.right), 0);
    }
    private void OnEqualizerPlotDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, EqualizerPlot)) return;
        var position = e.GetPosition(EqualizerPlot);
        AddEqualizerPointAt(20 * Math.Pow(1000, Math.Clamp((position.X - 52) / EqWidth, 0, 1)), Math.Clamp(12 - (position.Y - 24) / EqHeight * 24, -12, 12));
        e.Handled = true;
    }
    private void AddEqualizerPointAt(double frequency, double gain)
    {
        if (!_settings.Equalizer.AddPoint(frequency, gain)) return;
        _selectedEqBand = _settings.Equalizer.ParametricBands.Length - 1;
        RebuildParametricPoints(); QueueEqualizerChange();
    }
    private void OnRemoveEqualizerPoint(object sender, RoutedEventArgs e)
    {
        if (!_settings.Equalizer.RemovePoint(_selectedEqBand)) return;
        RebuildParametricPoints(); QueueEqualizerChange();
    }
    private void MoveEqualizerPoint(int index, double x, double y)
    {
        _selectedEqBand = index;
        var band = _settings.Equalizer.ParametricBands[index];
        band.Frequency = Math.Round(20 * Math.Pow(1000, Math.Clamp((x - 52) / EqWidth, 0, 1)), 1);
        if (band.Filter is not (EqualizerFilter.HighPass or EqualizerFilter.LowPass))
            band.GainDb = Math.Round(Math.Clamp(12 - (y - 24) / EqHeight * 24, -12, 12), 1);
        ParametricBandEdited();
    }
    private void ChangeEqualizerWidth(double x)
    {
        var frequency = 20 * Math.Pow(1000, Math.Clamp((x - 52) / EqWidth, 0, 1));
        _settings.Equalizer.ParametricBands[_selectedEqBand].SetWidthFromEdge(frequency);
        ParametricBandEdited();
    }
    private void PositionEqualizerPoint(int i)
    {
        var band = _settings.Equalizer.ParametricBands[i];
        Canvas.SetLeft(_eqNodes[i], EqX(band.Frequency) - 14);
        Canvas.SetTop(_eqNodes[i], EqY(band.Filter is EqualizerFilter.HighPass or EqualizerFilter.LowPass ? 0 : band.GainDb) - 14);
    }
    private void PositionEqualizerWidth()
    {
        if (_eqWidthLeft == null) return;
        var band = _settings.Equalizer.ParametricBands[_selectedEqBand];
        var left = EqX(Math.Clamp(band.Frequency / band.WidthRatio, 20, 20000));
        var right = EqX(Math.Clamp(band.Frequency * band.WidthRatio, 20, 20000));
        var y = EqY(band.Filter is EqualizerFilter.HighPass or EqualizerFilter.LowPass ? 0 : band.GainDb);
        var brush = new SolidColorBrush(EqColor(_selectedEqBand));
        _eqWidthLine.X1 = left; _eqWidthLine.X2 = right; _eqWidthLine.Y1 = _eqWidthLine.Y2 = y; _eqWidthLine.Stroke = brush;
        _eqWidthLeft.Background = _eqWidthRight.Background = brush;
        Canvas.SetLeft(_eqWidthLeft, left - 12); Canvas.SetTop(_eqWidthLeft, y - 12);
        Canvas.SetLeft(_eqWidthRight, right - 12); Canvas.SetTop(_eqWidthRight, y - 12);
        _eqWidthLeft.BorderBrush = _eqWidthRight.BorderBrush = brush;
    }
    private void SelectEqualizerBand(int index, bool render = true)
    {
        _selectedEqBand = Math.Clamp(index, 0, _settings.Equalizer.ParametricBands.Length - 1);
        var band = _settings.Equalizer.ParametricBands[_selectedEqBand];
        EqualizerAddPoint.IsEnabled = _settings.Equalizer.ParametricBands.Length < EqualizerSettings.MaxParametricBands;
        EqualizerRemovePoint.IsEnabled = _settings.Equalizer.ParametricBands.Length > 1;
        var loading = _equalizerLoading; _equalizerLoading = true;
        EqualizerBandSelector.SelectedIndex = _selectedEqBand;
        EqualizerBandEnabled.IsOn = band.Enabled;
        EqualizerBandFrequency.Value = Math.Round(band.Frequency, 1);
        EqualizerBandGain.Value = Math.Round(band.GainDb, 1);
        EqualizerBandQ.Value = Math.Round(band.Q, 3);
        EqualizerBandType.SelectedIndex = (int)band.Filter;
        EqualizerBandGain.IsEnabled = band.Filter is not (EqualizerFilter.HighPass or EqualizerFilter.LowPass);
        _equalizerLoading = loading;
        if (render) RenderEqualizerCurve();
    }
    private void ParametricBandEdited()
    {
        SelectEqualizerBand(_selectedEqBand, render: false);
        PositionEqualizerPoint(_selectedEqBand);
        PositionEqualizerWidth();
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
        var color = new SolidColorBrush(EqColor(_selectedEqBand));
        EqualizerSelectedCurve.Stroke = color; EqualizerCurveFill.Fill = color;
        EqualizerHeadroom.Text = maxDb > 0.5 ? $"Усиление до +{maxDb:F1} dB. Если голос искажается, уменьшите общую громкость." : "Есть запас громкости без усиления.";
        for (var i = 0; i < _eqNodes.Count; i++)
        {
            if (_eqNodes[i] == null) continue;
            var band = _settings.Equalizer.ParametricBands[i]; var node = _eqNodes[i];
            PositionEqualizerPoint(i);
            node.Opacity = band.Enabled ? 1 : .35;
            node.BorderBrush = i == _selectedEqBand ? Application.Current.Resources["VoiSee.PrimaryTextBrush"] as Brush : new SolidColorBrush(EqColor(i));
            ToolTipService.SetToolTip(node, $"{i + 1}: {band.Frequency:F0} Hz · {band.GainDb:+0.0;-0.0;0} dB · ширина Q {band.Q:F2}");
        }
        PositionEqualizerWidth();
    }
}
