using Microsoft.UI.Xaml.Controls;

namespace VoiSe.App;

public sealed partial class MainWindow
{
    private static void LayoutLocalizedEffectLibrary(Grid grid)
    {
        // Leave enough room for names in either language before adding a second column.
        var columns = System.Math.Clamp((int)(grid.ActualWidth / 320), 1, 2);
        var rows = (grid.Children.Count + columns - 1) / columns;
        if (grid.ColumnDefinitions.Count == columns && grid.RowDefinitions.Count == rows) return;
        grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = Microsoft.UI.Xaml.GridLength.Auto });
        for (var i = 0; i < grid.Children.Count; i++) { Grid.SetColumn((Microsoft.UI.Xaml.FrameworkElement)grid.Children[i], i % columns); Grid.SetRow((Microsoft.UI.Xaml.FrameworkElement)grid.Children[i], i / columns); }
    }
    private bool _languageLoading = true;
    private void InitializeLanguageSelection()
    {
        InterfaceLanguageComboBox.SelectedIndex = UiText.LanguageCode == "en" ? 1 : 0;
        _languageLoading = false;
    }
    private void OnInterfaceLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_languageLoading || _loadingSettings) return;
        _settings.InterfaceLanguage = InterfaceLanguageComboBox.SelectedIndex == 1 ? "en" : "ru";
        SaveCurrentSettings();
        LanguageRestartHint.Visibility = UiText.NormalizeLanguage(_settings.InterfaceLanguage) != UiText.LanguageCode
            ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}
