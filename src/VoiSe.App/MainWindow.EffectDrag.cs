using System;
using System.Globalization;
using System.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace VoiSe.App;

public sealed partial class MainWindow
{
    private bool _effectPointerDragActive;
    private bool _effectPointerDragFromChain;
    private string? _effectPointerDragKey;
    private Border? _effectPointerDragVisual;
    private uint _effectPointerDragPointerId;
    private double _effectPointerDragOffsetX;
    private double _effectPointerDragOffsetY;
    private UIElement? _effectPointerDragSource;
    private static readonly ResourceManager EffectDragResources = new("VoiSe.App.Resources.EffectDrag", typeof(MainWindow).Assembly);
    private static string EffectDragText(string key) => EffectDragResources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    private void InitializeEffectDrag()
    {
        EffectLibraryDragInstructions.Text = EffectDragText("Instructions");
        EffectLibraryReturnTitle.Text = EffectDragText("ReturnTitle");
        EffectLibraryReturnDescription.Text = EffectDragText("ReturnDescription");
        RootGrid.PreviewKeyDown += (_, e) =>
        {
            if (_effectPointerDragActive && e.Key == VirtualKey.Escape)
            { EndEffectPointerDrag(); e.Handled = true; }
        };
    }
    private void OnEffectLibraryGripPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string effectKey } grip)
        {
            return;
        }

        StartEffectPointerDrag(grip, effectKey, fromChain: false, e);
    }

    private void OnEffectChainGripPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string effectKey } grip)
        {
            return;
        }

        StartEffectPointerDrag(grip, effectKey, fromChain: true, e);
    }

    private void StartEffectPointerDrag(UIElement grip, string effectKey, bool fromChain, PointerRoutedEventArgs e)
    {
        if (EffectDragOverlay is null || _effectPointerDragActive || !ReferenceEquals(MainTabView.SelectedItem, VoiceChangerTab))
        {
            return;
        }

        var point = e.GetCurrentPoint(grip);
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed) return;
        if (!grip.CapturePointer(e.Pointer)) return;
        _effectPointerDragSource = grip;
        _effectPointerDragActive = true;
        _effectPointerDragFromChain = fromChain;
        _effectPointerDragKey = effectKey;
        _effectPointerDragPointerId = e.Pointer.PointerId;

        var sourceCard = FindVisualAncestor<Border>(grip);
        var width = Clamp(sourceCard?.ActualWidth ?? 460.0, 300.0, 520.0);
        var value = fromChain
            ? GetActiveEffectValue(effectKey)
            : GetLibraryEffectValue(effectKey);

        _effectPointerDragVisual = CreateEffectDragVisual(effectKey, value, width);
        EffectDragOverlay.Children.Add(_effectPointerDragVisual);
        Canvas.SetZIndex(_effectPointerDragVisual, 1000);

        _effectPointerDragOffsetX = Math.Min(34.0, width * 0.18);
        _effectPointerDragOffsetY = 24.0;
        UpdateEffectDragVisualPosition(e);
        e.Handled = true;
    }

    private Border CreateEffectDragVisual(string effectKey, double value, double width)
    {
        var card = new Border
        {
            Width = width,
            MinHeight = 86,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(7),
            Background = Application.Current.Resources["VoiSee.PanelBackgroundBrush"] as Brush,
            BorderBrush = Application.Current.Resources["VoiSee.PanelBorderBrush"] as Brush,
            BorderThickness = new Thickness(1.0),
            Opacity = 0.76,
            IsHitTestVisible = false
        };

        var stack = new StackPanel { Spacing = 8 };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "⋮⋮", Opacity = 0.72, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        var title = new TextBlock
        {
            Text = GetEffectDisplayName(effectKey),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var valueText = new TextBlock
        {
            Text = ((int)Math.Round(value)).ToString(),
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueText, 2);
        header.Children.Add(valueText);
        stack.Children.Add(header);

        var range = GetEffectUiRange(effectKey);
        var progress = new ProgressBar
        {
            Minimum = range.Minimum,
            Maximum = range.Maximum,
            Value = Clamp(value, range.Minimum, range.Maximum),
            Height = 5,
            IsIndeterminate = false
        };
        stack.Children.Add(progress);
        card.Child = stack;
        return card;
    }

    private void OnEffectDragPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_effectPointerDragActive || e.Pointer.PointerId != _effectPointerDragPointerId)
        {
            return;
        }

        UpdateEffectDragVisualPosition(e);
        e.Handled = true;
    }

    private void UpdateEffectDragVisualPosition(PointerRoutedEventArgs e)
    {
        if (EffectDragOverlay is null || _effectPointerDragVisual is null)
        {
            return;
        }

        var position = e.GetCurrentPoint(EffectDragOverlay).Position;
        Canvas.SetLeft(_effectPointerDragVisual, position.X - _effectPointerDragOffsetX);
        Canvas.SetTop(_effectPointerDragVisual, position.Y - _effectPointerDragOffsetY);
        EffectLibraryReturnHint.Visibility = _effectPointerDragFromChain && IsPointerInside(e, RootGrid)
            && IsPointerInside(e, EffectLibrarySurface) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnEffectDragPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_effectPointerDragActive || e.Pointer.PointerId != _effectPointerDragPointerId)
        {
            return;
        }

        try
        {
            // Test the release position afresh: hovering alone never changes the chain.
            if (IsPointerInside(e, RootGrid)) CompleteEffectPointerDrop(
                IsPointerInside(e, ProcessingChainSurface), IsPointerInside(e, EffectLibrarySurface), GetEffectInsertionIndex(e));
        }
        finally
        {
            EndEffectPointerDrag();
        }

        e.Handled = true;
    }

    private void OnEffectDragPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_effectPointerDragActive || e.Pointer.PointerId != _effectPointerDragPointerId) return;
        EndEffectPointerDrag();
        e.Handled = true;
    }

    private void OnEffectDragPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_effectPointerDragActive && e.Pointer.PointerId == _effectPointerDragPointerId) EndEffectPointerDrag();
    }

    private void CompleteEffectPointerDrop(bool insideChain, bool insideLibrary, int insertionIndex)
    {
        if (!_effectPointerDragActive || !ReferenceEquals(MainTabView.SelectedItem, VoiceChangerTab)
            || string.IsNullOrWhiteSpace(_effectPointerDragKey)) return;
        var key = _effectPointerDragKey;
        if (_effectPointerDragFromChain && insideLibrary) RemoveEffectFromProcessingChain(key);
        else if (insideChain)
        {
            if (_effectPointerDragFromChain) MoveEffectWithinProcessingChain(key, insertionIndex);
            else AddEffectToProcessingChain(key, insertionIndex);
        }
    }

    private void EndEffectPointerDrag()
    {
        // Clear state before releasing capture, which raises PointerCaptureLost synchronously.
        var source = _effectPointerDragSource;
        _effectPointerDragSource = null;
        _effectPointerDragActive = false;
        if (EffectLibraryReturnHint is not null) EffectLibraryReturnHint.Visibility = Visibility.Collapsed;
        if (_effectPointerDragVisual is not null && EffectDragOverlay is not null)
        {
            EffectDragOverlay.Children.Remove(_effectPointerDragVisual);
        }

        _effectPointerDragActive = false;
        _effectPointerDragFromChain = false;
        _effectPointerDragKey = null;
        _effectPointerDragVisual = null;
        _effectPointerDragPointerId = 0;
        source?.ReleasePointerCaptures();
    }

    private static bool IsPointerInside(PointerRoutedEventArgs e, FrameworkElement element)
    {
        var point = e.GetCurrentPoint(element).Position;
        return point.X >= 0 && point.Y >= 0 && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;
    }

    private int GetEffectInsertionIndex(PointerRoutedEventArgs e)
    {
        if (EffectChainCardsPanel is null || EffectChainCardsPanel.Children.Count == 0)
        {
            return 0;
        }

        var point = e.GetCurrentPoint(EffectChainCardsPanel).Position;
        for (var index = 0; index < EffectChainCardsPanel.Children.Count; index++)
        {
            if (EffectChainCardsPanel.Children[index] is not FrameworkElement child)
            {
                continue;
            }

            try
            {
                var top = child.TransformToVisual(EffectChainCardsPanel)
                    .TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                if (point.Y < top + child.ActualHeight / 2.0)
                {
                    return index;
                }
            }
            catch
            {
                // If the visual is being rebuilt, append at the end.
            }
        }

        return EffectChainCardsPanel.Children.Count;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

}
