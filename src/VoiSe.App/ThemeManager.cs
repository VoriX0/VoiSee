using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.UI;

namespace VoiSe.App;

public sealed class ThemeManager
{
    private static readonly Regex RuleRegex = new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex CommentRegex = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex VarRegex = new(@"var\((?<name>--[A-Za-z0-9_-]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<FrameworkElement, ElementStyleSnapshot> _snapshots = new();
    private readonly HashSet<FrameworkElement> _styledByLastApply = new();
    private readonly Dictionary<FrameworkElement, InteractiveThemeState> _interactiveStates = new();
    private readonly HashSet<FrameworkElement> _interactiveHandlersAttached = new();

    public ThemeManager(string dataDirectory)
    {
        ThemesDirectory = Path.Combine(dataDirectory, "themes");
    }

    public string ThemesDirectory { get; }

    public void EnsureThemesDirectory()
    {
        Directory.CreateDirectory(ThemesDirectory);
    }

    public IReadOnlyList<string> GetThemeFiles()
    {
        EnsureThemesDirectory();
        return Directory.GetFiles(ThemesDirectory, "*.voiseetheme.css", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string CreateNewThemeFile()
    {
        EnsureThemesDirectory();
        var basePath = Path.Combine(ThemesDirectory, "MyTheme.voiseetheme.css");
        var path = basePath;
        var index = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(ThemesDirectory, $"MyTheme {index}.voiseetheme.css");
            index++;
        }

        File.WriteAllText(path, CreateTemplateCss("My Theme"), Encoding.UTF8);
        return path;
    }

    public string ImportTheme(string sourcePath)
    {
        EnsureThemesDirectory();
        var fileName = Path.GetFileName(sourcePath);
        if (!fileName.EndsWith(".voiseetheme.css", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName) + ".voiseetheme.css";
        }

        var targetPath = Path.Combine(ThemesDirectory, fileName);
        if (File.Exists(targetPath))
        {
            var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));
            var index = 2;
            do
            {
                targetPath = Path.Combine(ThemesDirectory, $"{name} {index}.voiseetheme.css");
                index++;
            }
            while (File.Exists(targetPath));
        }

        File.Copy(sourcePath, targetPath);
        return targetPath;
    }

    public string ExportTheme(string? activeThemePath, string targetPath)
    {
        var css = !string.IsNullOrWhiteSpace(activeThemePath) && File.Exists(activeThemePath)
            ? File.ReadAllText(activeThemePath)
            : CreateTemplateCss("Default Dark");
        File.WriteAllText(targetPath, css, Encoding.UTF8);
        return targetPath;
    }

    public VoiSeeCssTheme LoadTheme(string? themePath)
    {
        if (string.IsNullOrWhiteSpace(themePath) || !File.Exists(themePath))
        {
            return new VoiSeeCssTheme("Default Dark", null);
        }

        var css = File.ReadAllText(themePath);
        return Parse(css, Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(themePath)), themePath);
    }

    public VoiSeeCssTheme Parse(string css, string fallbackName, string? sourcePath)
    {
        css = CommentRegex.Replace(css ?? string.Empty, string.Empty);
        var theme = new VoiSeeCssTheme(fallbackName, sourcePath);

        foreach (Match match in RuleRegex.Matches(css))
        {
            var selectorList = match.Groups["selector"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var declarations = ParseDeclarations(match.Groups["body"].Value);
            if (declarations.Count == 0)
            {
                continue;
            }

            foreach (var selector in selectorList)
            {
                var normalizedSelector = selector.Trim();
                if (string.IsNullOrWhiteSpace(normalizedSelector))
                {
                    continue;
                }

                if (normalizedSelector.Equals(":root", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var pair in declarations)
                    {
                        if (pair.Key.StartsWith("--", StringComparison.Ordinal))
                        {
                            theme.Variables[pair.Key] = pair.Value;
                        }
                    }
                }
                else
                {
                    theme.Rules.Add(new VoiSeeCssRule(normalizedSelector, new Dictionary<string, string>(declarations, StringComparer.OrdinalIgnoreCase)));
                }
            }
        }

        return theme;
    }

    private static Dictionary<string, string> ParseDeclarations(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawDeclaration in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = rawDeclaration.IndexOf(':');
            if (separator <= 0 || separator >= rawDeclaration.Length - 1)
            {
                continue;
            }

            var name = rawDeclaration[..separator].Trim();
            var value = rawDeclaration[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
            {
                result[name] = value;
            }
        }

        return result;
    }

    public int ApplyTheme(FrameworkElement root, VoiSeeCssTheme theme)
    {
        RestorePreviouslyStyledElements();
        _interactiveStates.Clear();

        var applied = 0;
        var styledThisApply = new HashSet<FrameworkElement>();
        var elements = EnumerateVisualTree(root).OfType<FrameworkElement>().ToArray();

        foreach (var element in elements)
        {
            applied += ApplyRulesToElement(element, theme, styledThisApply);
        }

        _styledByLastApply.Clear();
        foreach (var element in styledThisApply)
        {
            _styledByLastApply.Add(element);
        }

        foreach (var element in _interactiveStates.Keys.ToArray())
        {
            if (_interactiveStates.TryGetValue(element, out var state) && state.PseudoDeclarations.Count > 0)
            {
                AttachInteractiveHandlers(element);
                ApplyCurrentInteractiveState(element);
            }
        }

        return applied;
    }

    private void RestorePreviouslyStyledElements()
    {
        foreach (var element in _styledByLastApply.ToArray())
        {
            if (_snapshots.TryGetValue(element, out var snapshot))
            {
                snapshot.Restore(element);
            }
        }

        _styledByLastApply.Clear();
    }

    private int ApplyRulesToElement(FrameworkElement element, VoiSeeCssTheme theme, HashSet<FrameworkElement> styledThisApply)
    {
        var applied = 0;
        foreach (var rule in theme.Rules)
        {
            var selector = ParseSelector(rule.Selector);
            if (!IsSelectorMatch(element, selector.BaseSelector))
            {
                continue;
            }

            var resolved = rule.Declarations.ToDictionary(
                pair => pair.Key,
                pair => theme.ResolveValue(pair.Value),
                StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(selector.Pseudo))
            {
                var pseudo = NormalizePseudo(selector.Pseudo);
                if (pseudo is not null)
                {
                    var state = GetInteractiveState(element);
                    state.PseudoDeclarations[pseudo] = resolved;
                    CaptureSnapshotIfNeeded(element);
                    styledThisApply.Add(element);
                }
                continue;
            }

            foreach (var declaration in resolved)
            {
                CaptureSnapshotIfNeeded(element);
                if (ApplyDeclaration(element, declaration.Key, declaration.Value))
                {
                    applied++;
                    styledThisApply.Add(element);
                    GetInteractiveState(element).BaseDeclarations[declaration.Key] = declaration.Value;
                }
            }
        }

        return applied;
    }

    private static ParsedSelector ParseSelector(string selector)
    {
        selector = selector.Trim();
        if (selector.Equals(":root", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedSelector(selector, null);
        }

        var colon = selector.LastIndexOf(':');
        if (colon > 0 && colon < selector.Length - 1)
        {
            return new ParsedSelector(selector[..colon].Trim(), selector[(colon + 1)..].Trim());
        }

        return new ParsedSelector(selector, null);
    }

    private static string? NormalizePseudo(string pseudo)
    {
        return pseudo.Trim().ToLowerInvariant() switch
        {
            "hover" => "hover",
            "pressed" or "active" or "onclick" or "on-click" => "pressed",
            "checked" or "on" => "checked",
            _ => null
        };
    }

    private static bool IsSelectorMatch(FrameworkElement element, string selector)
    {
        selector = selector.Trim();
        if (string.IsNullOrWhiteSpace(selector))
        {
            return false;
        }

        if (selector.StartsWith("#", StringComparison.Ordinal))
        {
            var id = selector[1..];
            return GetElementIds(element).Contains(id, StringComparer.OrdinalIgnoreCase);
        }

        if (selector.StartsWith(".", StringComparison.Ordinal))
        {
            var cls = selector[1..];
            return GetElementClasses(element).Contains(cls, StringComparer.OrdinalIgnoreCase);
        }

        return element.GetType().Name.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetElementIds(FrameworkElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.Name))
        {
            yield return element.Name;
        }

        foreach (var alias in GetFriendlyIds(element))
        {
            yield return alias;
        }
    }

    private static IEnumerable<string> GetFriendlyIds(FrameworkElement element)
    {
        var name = element.Name ?? string.Empty;
        switch (name)
        {
            case "SoundBoardTabRoot": yield return "MainSoundboard"; break;
            case "VoiceChangerScrollViewer": yield return "MainVoiceChanger"; break;
            case "ScenesTabRoot": yield return "MainScenes"; break;
            case "MainSettings": yield return "MainSettings"; break;
            case "SettingsThemesPanel": yield return "MainThemes"; yield return "ThemesPanel"; break;
            case "AboutMePanel": yield return "AboutMePanel"; break;
            case "VBCableNoticeBorder": yield return "VBCableNoticeBorder"; yield return "SettingsVBCable"; break;
            case "MainHeaderBorder": yield return "MainHeaderBorder"; yield return "HeaderPanel"; break;
            case "MainTabHostBorder": yield return "MainTabs"; break;
            case "TimelineHost": yield return "SoundboardTimeline"; yield return "SoungboardTimeline"; break;
            case "SoundListArea": yield return "SoundboardSoundList"; break;
            case "CategoryComboBox": yield return "SoundboardCategoryList"; break;
            case "NextSoundButton": yield return "SoundboardNext"; break;
            case "PreviousSoundButton": yield return "SoundboardPrevious"; break;
            case "PlayPauseButton": yield return "SoundboardPlayPause"; break;
            case "StopSoundButton": yield return "SoundboardStop"; break;
            case "SoundLoopToggleButton": yield return "SoundboardLoop"; break;
            case "SoundVirtualVolumeSlider": yield return "SoundboardVirtualMic"; break;
            case "SoundMonitorVolumeSlider": yield return "SoundboardHeadphones"; break;
            case "SoundVirtualDelaySlider": yield return "SoundboardDelay"; break;
            case "VirtualOutputVolumeSlider": yield return "SettingsVirtualMicMaster"; break;
            case "VirtualMicMuteToggleButton": yield return "SettingsMute"; yield return "GlobalMute"; break;
            case "StartEngineButton": yield return "SettingsStartEngine"; break;
            case "StopEngineButton": yield return "SettingsStopEngine"; break;
            case "VoiceMonitorButton": yield return "VoicechangerMonitor"; break;
            case "VoicePresetsPanel": yield return "VoiceChangerPresets"; yield return "VoicechangerPresets"; break;
            case "SceneApplyButton": yield return "ScenesApply"; break;
            case "SceneDisableButton": yield return "ScenesDisable"; break;
            case "SceneDeleteButton": yield return "ScenesDelete"; break;
            case "SceneRenameButton": yield return "ScenesRename"; break;
            case "SceneCreateNewButton": yield return "ScenesCreate"; break;
            case "SceneVoicePresetComboBox": yield return "ScenesVoicePreset"; break;
            case "SceneVoiceMonitorButton": yield return "ScenesVoiceMonitor"; break;
            case "SceneLoopPlayLoopButton": yield return "ScenesLoopPlayLoop"; break;
            case "SceneLoopPlayOnceButton": yield return "ScenesLoopPlayOnce"; break;
            case "SceneLoopRemoveButton": yield return "ScenesLoopRemove"; break;
            case "SceneLoopChooseButton": yield return "ScenesLoopChoose"; break;
        }
    }

    private static IEnumerable<string> GetElementClasses(FrameworkElement element)
    {
        yield return "all";

        var area = DetectArea(element);
        if (!string.IsNullOrWhiteSpace(area))
        {
            yield return area;
        }

        if (element is Grid or StackPanel or ScrollViewer or VariableSizedWrapGrid)
        {
            yield return "layout";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-layout";
        }

        if (element is Border)
        {
            yield return "panel";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-panel";
        }

        if (element is TextBlock)
        {
            yield return "text";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-text";
        }

        if (element is Button)
        {
            yield return "button";
            yield return "primary-button";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-button";
        }

        if (element is ToggleButton)
        {
            yield return "button";
            yield return "toggle-button";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-button";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-toggle-button";
        }

        if (element is HyperlinkButton)
        {
            yield return "link-button";
            yield return "button";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-button";
        }

        if (element is ComboBox)
        {
            yield return "combo-box";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-combo-box";
        }

        if (element is Slider)
        {
            yield return "slider";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-slider";
        }

        if (element is TextBox)
        {
            yield return "text-box";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-text-box";
        }

        if (element is TabView)
        {
            yield return "tab-view";
        }

        if (element is TabViewItem)
        {
            yield return "tab-item";
        }

        if (element is ListView)
        {
            yield return "list-view";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-list-view";
        }

        if (element.Name.Contains("Banner", StringComparison.OrdinalIgnoreCase))
        {
            yield return "status-banner";
        }

        if (element.Tag is SoundBoardSound)
        {
            yield return "soundboard-sound";
            yield return "soundboard-row";
        }
    }

    private static string? DetectArea(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement fe)
            {
                var name = fe.Name ?? string.Empty;
                if (name.Equals("SoundBoardTabRoot", StringComparison.OrdinalIgnoreCase) || name.Equals("SoundItemsPanel", StringComparison.OrdinalIgnoreCase) || name.Equals("SoundBoardBodyGrid", StringComparison.OrdinalIgnoreCase))
                {
                    return "soundboard";
                }
                if (name.Equals("VoiceChangerScrollViewer", StringComparison.OrdinalIgnoreCase) || name.Equals("VoicePresetsPanel", StringComparison.OrdinalIgnoreCase))
                {
                    return "voicechanger";
                }
                if (name.Equals("ScenesTabRoot", StringComparison.OrdinalIgnoreCase) || name.Equals("SceneSoundsPanel", StringComparison.OrdinalIgnoreCase) || name.Equals("LoopedSceneSoundsPanel", StringComparison.OrdinalIgnoreCase))
                {
                    return "scenes";
                }
                if (name.Equals("SettingsScrollViewer", StringComparison.OrdinalIgnoreCase) || name.Equals("SettingsTabRoot", StringComparison.OrdinalIgnoreCase) || name.Equals("MainSettings", StringComparison.OrdinalIgnoreCase) || name.Equals("SettingsThemesPanel", StringComparison.OrdinalIgnoreCase) || name.Equals("AboutMePanel", StringComparison.OrdinalIgnoreCase))
                {
                    return "settings";
                }
            }
        }

        return null;
    }

    private void CaptureSnapshotIfNeeded(FrameworkElement element)
    {
        if (!_snapshots.ContainsKey(element))
        {
            _snapshots[element] = ElementStyleSnapshot.Capture(element);
        }
    }

    private InteractiveThemeState GetInteractiveState(FrameworkElement element)
    {
        if (!_interactiveStates.TryGetValue(element, out var state))
        {
            state = new InteractiveThemeState();
            _interactiveStates[element] = state;
        }
        return state;
    }

    private void AttachInteractiveHandlers(FrameworkElement element)
    {
        if (_interactiveHandlersAttached.Contains(element))
        {
            return;
        }

        element.PointerEntered += OnThemedElementPointerEntered;
        element.PointerExited += OnThemedElementPointerExited;
        element.PointerPressed += OnThemedElementPointerPressed;
        element.PointerReleased += OnThemedElementPointerReleased;
        element.PointerCanceled += OnThemedElementPointerReleased;
        element.PointerCaptureLost += OnThemedElementPointerReleased;

        if (element is ToggleButton toggle)
        {
            toggle.Checked += OnThemedToggleChanged;
            toggle.Unchecked += OnThemedToggleChanged;
        }

        _interactiveHandlersAttached.Add(element);
    }

    private void OnThemedElementPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && _interactiveStates.TryGetValue(element, out var state))
        {
            state.IsHover = true;
            ApplyCurrentInteractiveState(element);
        }
    }

    private void OnThemedElementPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && _interactiveStates.TryGetValue(element, out var state))
        {
            state.IsHover = false;
            state.IsPressed = false;
            ApplyCurrentInteractiveState(element);
        }
    }

    private void OnThemedElementPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && _interactiveStates.TryGetValue(element, out var state))
        {
            state.IsPressed = true;
            ApplyCurrentInteractiveState(element);
        }
    }

    private void OnThemedElementPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && _interactiveStates.TryGetValue(element, out var state))
        {
            state.IsPressed = false;
            ApplyCurrentInteractiveState(element);
        }
    }

    private void OnThemedToggleChanged(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ApplyCurrentInteractiveState(element);
        }
    }

    private void ApplyCurrentInteractiveState(FrameworkElement element)
    {
        if (!_interactiveStates.TryGetValue(element, out var state))
        {
            return;
        }

        if (_snapshots.TryGetValue(element, out var snapshot))
        {
            snapshot.Restore(element);
        }

        ApplyDeclarations(element, state.BaseDeclarations);

        if (state.IsHover && state.PseudoDeclarations.TryGetValue("hover", out var hover))
        {
            ApplyDeclarations(element, hover);
        }

        var isChecked = element is ToggleButton { IsChecked: true };
        if (isChecked && state.PseudoDeclarations.TryGetValue("checked", out var checkedDeclarations))
        {
            ApplyDeclarations(element, checkedDeclarations);
        }

        if (state.IsPressed && state.PseudoDeclarations.TryGetValue("pressed", out var pressed))
        {
            ApplyDeclarations(element, pressed);
        }
    }

    private static void ApplyDeclarations(FrameworkElement element, IReadOnlyDictionary<string, string> declarations)
    {
        foreach (var declaration in declarations)
        {
            ApplyDeclaration(element, declaration.Key, declaration.Value);
        }
    }

    private static bool ApplyDeclaration(FrameworkElement element, string property, string value)
    {
        try
        {
            switch (property.Trim().ToLowerInvariant())
            {
                case "background":
                case "background-color":
                case "background-image":
                    return ApplyBackground(element, value);
                case "foreground":
                case "color":
                    return ApplyForeground(element, value);
                case "border-color":
                case "border":
                    return ApplyBorderBrush(element, value);
                case "border-thickness":
                    return ApplyBorderThickness(element, value);
                case "corner-radius":
                case "border-radius":
                    return ApplyCornerRadius(element, value);
                case "opacity":
                    if (TryParseDouble(value, out var opacity))
                    {
                        element.Opacity = Math.Clamp(opacity, 0.0, 1.0);
                        return true;
                    }
                    return false;
                case "font-size":
                    return ApplyFontSize(element, value);
                case "font-weight":
                    return ApplyFontWeight(element, value);
                case "padding":
                    return ApplyPadding(element, value);
                case "margin":
                    return ApplyMargin(element, value);
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool ApplyBackground(FrameworkElement element, string value)
    {
        if (!TryParseBrush(value, out var brush)) return false;
        switch (element)
        {
            case Panel panel:
                panel.Background = brush;
                return true;
            case Border border:
                border.Background = brush;
                return true;
            case Control control:
                control.Background = brush;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyForeground(FrameworkElement element, string value)
    {
        if (!TryParseBrush(value, out var brush)) return false;
        switch (element)
        {
            case TextBlock textBlock:
                textBlock.Foreground = brush;
                return true;
            case Control control:
                control.Foreground = brush;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyBorderBrush(FrameworkElement element, string value)
    {
        if (!TryParseBrush(value, out var brush)) return false;
        switch (element)
        {
            case Border border:
                border.BorderBrush = brush;
                return true;
            case Control control:
                control.BorderBrush = brush;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyBorderThickness(FrameworkElement element, string value)
    {
        if (!TryParseThickness(value, out var thickness)) return false;
        switch (element)
        {
            case Border border:
                border.BorderThickness = thickness;
                return true;
            case Control control:
                control.BorderThickness = thickness;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyCornerRadius(FrameworkElement element, string value)
    {
        if (!TryParseCornerRadius(value, out var radius)) return false;
        if (element is Border border)
        {
            border.CornerRadius = radius;
            return true;
        }

        return false;
    }

    private static bool ApplyPadding(FrameworkElement element, string value)
    {
        if (!TryParseThickness(value, out var padding)) return false;
        switch (element)
        {
            case Border border:
                border.Padding = padding;
                return true;
            case Control control:
                control.Padding = padding;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyMargin(FrameworkElement element, string value)
    {
        if (!TryParseThickness(value, out var margin)) return false;
        element.Margin = margin;
        return true;
    }

    private static bool ApplyFontSize(FrameworkElement element, string value)
    {
        if (!TryParseDouble(value, out var size) || size <= 0) return false;
        switch (element)
        {
            case TextBlock textBlock:
                textBlock.FontSize = size;
                return true;
            case Control control:
                control.FontSize = size;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyFontWeight(FrameworkElement element, string value)
    {
        var weight = value.Trim().ToLowerInvariant() switch
        {
            "bold" or "700" => Microsoft.UI.Text.FontWeights.Bold,
            "semibold" or "semi-bold" or "600" => Microsoft.UI.Text.FontWeights.SemiBold,
            "light" or "300" => Microsoft.UI.Text.FontWeights.Light,
            _ => Microsoft.UI.Text.FontWeights.Normal
        };

        switch (element)
        {
            case TextBlock textBlock:
                textBlock.FontWeight = weight;
                return true;
            case Control control:
                control.FontWeight = weight;
                return true;
            default:
                return false;
        }
    }

    public static bool TryParseColor(string value, out Color color)
    {
        value = value.Trim();
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = Color.FromArgb(0, 0, 0, 0);
            return true;
        }
        if (value.Equals("black", StringComparison.OrdinalIgnoreCase))
        {
            color = Color.FromArgb(255, 0, 0, 0);
            return true;
        }
        if (value.Equals("white", StringComparison.OrdinalIgnoreCase))
        {
            color = Color.FromArgb(255, 255, 255, 255);
            return true;
        }

        if (TryParseRgbFunction(value, out color))
        {
            return true;
        }

        if (!value.StartsWith("#", StringComparison.Ordinal))
        {
            color = default;
            return false;
        }

        var hex = value[1..];
        try
        {
            if (hex.Length == 6)
            {
                color = Color.FromArgb(255, byte.Parse(hex[..2], NumberStyles.HexNumber), byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber), byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
                return true;
            }
            if (hex.Length == 8)
            {
                color = Color.FromArgb(byte.Parse(hex[..2], NumberStyles.HexNumber), byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber), byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber), byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber));
                return true;
            }
        }
        catch
        {
        }

        color = default;
        return false;
    }

    private static bool TryParseRgbFunction(string value, out Color color)
    {
        color = default;
        var match = Regex.Match(value.Trim(), @"^(rgba?|RGBA?)\((?<args>.*)\)$");
        if (!match.Success)
        {
            return false;
        }

        var parts = SplitFunctionArgs(match.Groups["args"].Value);
        if (parts.Count < 3 || parts.Count > 4)
        {
            return false;
        }

        if (!TryParseByte(parts[0], out var r) || !TryParseByte(parts[1], out var g) || !TryParseByte(parts[2], out var b))
        {
            return false;
        }

        var a = (byte)255;
        if (parts.Count == 4)
        {
            var raw = parts[3].Trim();
            if (raw.EndsWith("%", StringComparison.Ordinal) && TryParseDouble(raw.TrimEnd('%'), out var percent))
            {
                a = (byte)Math.Clamp(Math.Round(percent * 2.55), 0, 255);
            }
            else if (TryParseDouble(raw, out var alpha))
            {
                a = alpha <= 1.0 ? (byte)Math.Clamp(Math.Round(alpha * 255.0), 0, 255) : (byte)Math.Clamp(Math.Round(alpha), 0, 255);
            }
        }

        color = Color.FromArgb(a, r, g, b);
        return true;
    }

    private static bool TryParseByte(string value, out byte result)
    {
        value = value.Trim();
        if (value.EndsWith("%", StringComparison.Ordinal) && TryParseDouble(value.TrimEnd('%'), out var percent))
        {
            result = (byte)Math.Clamp(Math.Round(percent * 2.55), 0, 255);
            return true;
        }
        if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryParseBrush(string value, out Brush brush)
    {
        value = value.Trim();
        if (TryParseLinearGradient(value, out var gradient))
        {
            brush = gradient;
            return true;
        }

        if (TryParseColor(value, out var color))
        {
            brush = new SolidColorBrush(color);
            return true;
        }

        brush = null!;
        return false;
    }

    private static bool TryParseLinearGradient(string value, out LinearGradientBrush brush)
    {
        brush = null!;
        var match = Regex.Match(value.Trim(), @"^linear-gradient\((?<args>.*)\)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        var args = SplitFunctionArgs(match.Groups["args"].Value);
        if (args.Count < 2)
        {
            return false;
        }

        var angle = 90.0;
        var colorStartIndex = 0;
        if (args[0].Trim().EndsWith("deg", StringComparison.OrdinalIgnoreCase) && TryParseDouble(args[0].Trim()[..^3], out var parsedAngle))
        {
            angle = parsedAngle;
            colorStartIndex = 1;
        }

        if (args.Count - colorStartIndex < 2)
        {
            return false;
        }

        var colors = new List<Color>();
        for (var i = colorStartIndex; i < args.Count; i++)
        {
            var colorPart = args[i].Trim();
            var space = colorPart.IndexOf(' ');
            if (space > 0 && colorPart.StartsWith("#", StringComparison.Ordinal))
            {
                colorPart = colorPart[..space];
            }

            if (!TryParseColor(colorPart, out var color))
            {
                return false;
            }
            colors.Add(color);
        }

        brush = new LinearGradientBrush();
        ApplyGradientAngle(brush, angle);
        if (colors.Count == 1)
        {
            brush.GradientStops.Add(new GradientStop { Color = colors[0], Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = colors[0], Offset = 1 });
            return true;
        }

        for (var i = 0; i < colors.Count; i++)
        {
            brush.GradientStops.Add(new GradientStop
            {
                Color = colors[i],
                Offset = colors.Count == 1 ? 0 : i / (double)(colors.Count - 1)
            });
        }

        return true;
    }

    private static void ApplyGradientAngle(LinearGradientBrush brush, double angle)
    {
        var radians = angle * Math.PI / 180.0;
        var dx = Math.Cos(radians);
        var dy = Math.Sin(radians);
        brush.StartPoint = new Point(0.5 - dx / 2.0, 0.5 - dy / 2.0);
        brush.EndPoint = new Point(0.5 + dx / 2.0, 0.5 + dy / 2.0);
    }

    private static List<string> SplitFunctionArgs(string value)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == ',' && depth == 0)
            {
                result.Add(value[start..i].Trim());
                start = i + 1;
            }
        }
        result.Add(value[start..].Trim());
        return result.Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
    }

    private static bool TryParseDouble(string value, out double result)
    {
        return double.TryParse(value.Trim().Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseThickness(string value, out Thickness thickness)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && TryParseDouble(parts[0], out var all))
        {
            thickness = new Thickness(all);
            return true;
        }
        if (parts.Length == 2 && TryParseDouble(parts[0], out var vertical) && TryParseDouble(parts[1], out var horizontal))
        {
            thickness = new Thickness(horizontal, vertical, horizontal, vertical);
            return true;
        }
        if (parts.Length == 4 && TryParseDouble(parts[0], out var left) && TryParseDouble(parts[1], out var top) && TryParseDouble(parts[2], out var right) && TryParseDouble(parts[3], out var bottom))
        {
            thickness = new Thickness(left, top, right, bottom);
            return true;
        }

        thickness = default;
        return false;
    }

    private static bool TryParseCornerRadius(string value, out CornerRadius radius)
    {
        if (TryParseDouble(value, out var all))
        {
            radius = new CornerRadius(all);
            return true;
        }

        radius = default;
        return false;
    }

    private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
    {
        yield return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var descendant in EnumerateVisualTree(child))
            {
                yield return descendant;
            }
        }
    }

    public string CreateTemplateCss(string themeName)
    {
        return $$"""
/*
  VoiSee Theme CSS
  Theme: {{themeName}}

  This template is intentionally non-destructive: selector blocks are written for you,
  but visual declarations are commented out. Uncomment or add declarations and save the
  file; VoiSee reloads it automatically.

  Supported selectors:
    #ElementId, #FriendlyId, .class, Button, Border, TextBlock, Slider, ComboBox

  Useful classes:
    .panel, .button, .toggle-button, .slider, .combo-box, .text, .tab-view, .tab-item
    .soundboard-button, .soundboard-panel, .soundboard-slider, .soundboard-sound
    .voicechanger-button, .voicechanger-panel, .voicechanger-slider
    .scenes-button, .scenes-panel, .scenes-slider
    .settings-button, .settings-panel, .settings-slider, .settings-combo-box

  Pseudo states:
    :hover, :pressed / :onclick, :checked / :on

  Supported properties:
    background, foreground/color, border-color, border-thickness, corner-radius/border-radius,
    opacity, font-size, font-weight, padding, margin

  Supported color functions:
    #RRGGBB, #AARRGGBB, rgb(r,g,b), rgba(r,g,b,a), transparent, black, white,
    linear-gradient(90deg, #111111, #333333)

  Blocked by design: scripts, url(), external files, arbitrary code, and full layout CSS.
*/

:root {
  --app-background: #000000;
  --titlebar-background: #000000;
  --panel-background: #08000000;
  --panel-border: #22FFFFFF;
  --text-primary: #FFFFFF;
  --text-secondary: #B8FFFFFF;
  --accent: #00D5FF;
  --danger: #FF4D4D;
  --warning: #D68B00;
  --success: #37D67A;
  --button-background: #202020;
  --button-hover: #303030;
  --button-pressed: #101010;
  --button-border: #44FFFFFF;
  --corner-radius: 12;
}

/* Global app areas */
#RootGrid { /* background: var(--app-background); */ }
#CustomTitleBar { /* background: var(--titlebar-background); */ }
#MainHeaderBorder { /* background: var(--panel-background); border-color: var(--panel-border); border-thickness: 1; corner-radius: var(--corner-radius); */ }
#MainTabs { /* background: transparent; */ }
#VirtualMicMutedBanner { /* background: #44220000; border-color: #88FF4D4D; */ }

/* Main tab panels */
#MainSoundboard { /* background: transparent; */ }
#MainVoiceChanger { /* background: transparent; */ }
#MainScenes { /* background: transparent; */ }
#MainSettings { /* background: transparent; */ }
#MainThemes, #ThemesPanel { /* background: var(--panel-background); border-color: var(--panel-border); border-thickness: 1; corner-radius: var(--corner-radius); */ }
#AboutMePanel { /* background: var(--panel-background); border-color: var(--panel-border); border-thickness: 1; corner-radius: var(--corner-radius); */ }
#VBCableNoticeBorder, #SettingsVBCable { /* background: #22141414; border-color: #55D68B00; */ }

/* Global controls */
.text { /* foreground: var(--text-primary); */ }
.button { /* background: var(--button-background); foreground: var(--text-primary); border-color: var(--button-border); corner-radius: 8; */ }
.button:hover { /* background: var(--button-hover); */ }
.button:pressed { /* background: var(--button-pressed); */ }
.toggle-button:on { /* background: var(--accent); foreground: #000000; */ }
.slider { /* foreground: var(--accent); */ }
.combo-box { /* background: #181818; foreground: var(--text-primary); border-color: var(--button-border); corner-radius: 8; */ }
.link-button { /* foreground: var(--accent); */ }

/* Header / mute */
#SettingsMute, #GlobalMute { /* background: var(--button-background); */ }
#SettingsMute:hover { /* background: var(--button-hover); */ }
#SettingsMute:pressed { /* background: var(--danger); */ }
#VirtualMicMuteStatusTextBlock { /* foreground: var(--success); */ }

/* SoundBoard */
#SoundboardTimeline { /* background: linear-gradient(90deg, #252525, #111111); */ }
#SoundboardSoundList { /* background: transparent; */ }
#SoundboardCategoryList { /* background: #181818; */ }
#SoundboardNext, #SoundboardPrevious, #SoundboardPlayPause, #SoundboardStop, #SoundboardLoop { /* background: var(--button-background); */ }
.soundboard-button { /* background: var(--button-background); */ }
.soundboard-button:hover { /* background: var(--button-hover); */ }
.soundboard-button:pressed { /* background: var(--button-pressed); */ }
.soundboard-sound { /* background: transparent; corner-radius: 6; */ }
.soundboard-sound:hover { /* background: #18FFFFFF; */ }
#SoundboardVirtualMic, #SoundboardHeadphones, #SoundboardDelay { /* foreground: var(--accent); */ }

/* Voice Changer */
#VoiceChangerPresets, #VoicechangerPresets { /* background: transparent; */ }
#VoicechangerMonitor { /* background: var(--button-background); */ }
.voicechanger-button { /* background: var(--button-background); */ }
.voicechanger-button:hover { /* background: var(--button-hover); */ }
.voicechanger-slider { /* foreground: var(--accent); */ }

/* Scenes */
#ScenesApply, #ScenesDisable, #ScenesDelete, #ScenesRename, #ScenesCreate { /* background: var(--button-background); */ }
#ScenesApply:hover, #ScenesDisable:hover, #ScenesCreate:hover { /* background: var(--button-hover); */ }
#ScenesApply:pressed { /* background: var(--success); */ }
#ScenesDisable:pressed, #ScenesDelete:pressed { /* background: var(--danger); */ }
.scenes-button { /* background: var(--button-background); */ }
.scenes-button:hover { /* background: var(--button-hover); */ }
#ScenesVoicePreset { /* background: #181818; */ }
#ScenesLoopPlayLoop, #ScenesLoopPlayOnce, #ScenesLoopRemove, #ScenesLoopChoose { /* background: var(--button-background); */ }

/* Settings */
#SettingsStartEngine, #SettingsStopEngine { /* background: var(--button-background); */ }
#SettingsStartEngine:pressed { /* background: var(--success); */ }
#SettingsStopEngine:pressed { /* background: var(--danger); */ }
#SettingsVirtualMicMaster { /* foreground: var(--accent); */ }
.settings-button { /* background: var(--button-background); */ }
.settings-button:hover { /* background: var(--button-hover); */ }
.settings-panel { /* background: var(--panel-background); */ }
""";
    }

    private sealed record ParsedSelector(string BaseSelector, string? Pseudo);

    private sealed class InteractiveThemeState
    {
        public bool IsHover { get; set; }
        public bool IsPressed { get; set; }
        public Dictionary<string, string> BaseDeclarations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> PseudoDeclarations { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ElementStyleSnapshot
    {
        private Brush? Background { get; set; }
        private Brush? Foreground { get; set; }
        private Brush? BorderBrush { get; set; }
        private Thickness? BorderThickness { get; set; }
        private CornerRadius? CornerRadius { get; set; }
        private Thickness? Padding { get; set; }
        private Thickness Margin { get; set; }
        private double Opacity { get; set; }
        private double? FontSize { get; set; }
        private Windows.UI.Text.FontWeight? FontWeight { get; set; }

        public static ElementStyleSnapshot Capture(FrameworkElement element)
        {
            var snapshot = new ElementStyleSnapshot
            {
                Margin = element.Margin,
                Opacity = element.Opacity
            };

            switch (element)
            {
                case Panel panel:
                    snapshot.Background = panel.Background;
                    break;
                case Border border:
                    snapshot.Background = border.Background;
                    snapshot.BorderBrush = border.BorderBrush;
                    snapshot.BorderThickness = border.BorderThickness;
                    snapshot.CornerRadius = border.CornerRadius;
                    snapshot.Padding = border.Padding;
                    break;
                case Control control:
                    snapshot.Background = control.Background;
                    snapshot.Foreground = control.Foreground;
                    snapshot.BorderBrush = control.BorderBrush;
                    snapshot.BorderThickness = control.BorderThickness;
                    snapshot.Padding = control.Padding;
                    snapshot.FontSize = control.FontSize;
                    snapshot.FontWeight = control.FontWeight;
                    break;
                case TextBlock textBlock:
                    snapshot.Foreground = textBlock.Foreground;
                    snapshot.FontSize = textBlock.FontSize;
                    snapshot.FontWeight = textBlock.FontWeight;
                    break;
            }

            return snapshot;
        }

        public void Restore(FrameworkElement element)
        {
            element.Margin = Margin;
            element.Opacity = Opacity;
            switch (element)
            {
                case Panel panel:
                    panel.Background = Background;
                    break;
                case Border border:
                    border.Background = Background;
                    border.BorderBrush = BorderBrush;
                    if (BorderThickness.HasValue) border.BorderThickness = BorderThickness.Value;
                    if (CornerRadius.HasValue) border.CornerRadius = CornerRadius.Value;
                    if (Padding.HasValue) border.Padding = Padding.Value;
                    break;
                case Control control:
                    control.Background = Background;
                    control.Foreground = Foreground;
                    control.BorderBrush = BorderBrush;
                    if (BorderThickness.HasValue) control.BorderThickness = BorderThickness.Value;
                    if (Padding.HasValue) control.Padding = Padding.Value;
                    if (FontSize.HasValue) control.FontSize = FontSize.Value;
                    if (FontWeight.HasValue) control.FontWeight = FontWeight.Value;
                    break;
                case TextBlock textBlock:
                    textBlock.Foreground = Foreground;
                    if (FontSize.HasValue) textBlock.FontSize = FontSize.Value;
                    if (FontWeight.HasValue) textBlock.FontWeight = FontWeight.Value;
                    break;
            }
        }
    }
}

public sealed class VoiSeeCssTheme
{
    public VoiSeeCssTheme(string name, string? sourcePath)
    {
        Name = name;
        SourcePath = sourcePath;
    }

    public string Name { get; }
    public string? SourcePath { get; }
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<VoiSeeCssRule> Rules { get; } = new();

    public string ResolveValue(string value)
    {
        value = value.Trim();
        return Regex.Replace(value, @"var\((?<name>--[A-Za-z0-9_-]+)\)", match =>
        {
            var name = match.Groups["name"].Value;
            return Variables.TryGetValue(name, out var resolved) ? resolved.Trim() : match.Value;
        }, RegexOptions.IgnoreCase);
    }

    public bool TryGetVariableColor(string variableName, out Color color)
    {
        if (Variables.TryGetValue(variableName, out var value))
        {
            return ThemeManager.TryParseColor(ResolveValue(value), out color);
        }

        color = default;
        return false;
    }
}

public sealed record VoiSeeCssRule(string Selector, Dictionary<string, string> Declarations);
