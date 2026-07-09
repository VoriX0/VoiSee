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
        // Restore every element ever touched by the theme engine, not only the
        // elements from the previous visible tab. This prevents a newly-created
        // blank theme from inheriting colors/radius from the previously selected
        // theme and avoids stale styles when TabView virtualizes/unloads content.
        RestoreAllThemedElements();
        _interactiveStates.Clear();

        // A theme with no active rules is intentionally non-destructive. Restore
        // everything and clear captured snapshots, so the next non-empty theme
        // captures a clean XAML baseline instead of a previously themed baseline.
        if (theme.Rules.Count == 0)
        {
            _snapshots.Clear();
            _styledByLastApply.Clear();
            return 0;
        }

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

    private void RestoreAllThemedElements()
    {
        foreach (var pair in _snapshots.ToArray())
        {
            try
            {
                pair.Value.Restore(pair.Key);
            }
            catch
            {
                // Themed elements can be virtualized/recreated by WinUI while switching tabs.
                // A failed restore should never break theme application.
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
            // Global panels / areas (Pn = panel/container)
            case "RootGrid": yield return "PnRoot"; break;
            case "CustomTitleBar": yield return "PnTitleBar"; break;
            case "MainHeaderBorder": yield return "MainHeaderBorder"; yield return "HeaderPanel"; yield return "PnMainHeader"; yield return "PnHeader"; break;
            case "MainContentHost": yield return "PnMainContent"; break;
            case "MainTabHostBorder": yield return "MainTabs"; yield return "PnMainTabs"; break;
            case "SoundBoardTabRoot": yield return "MainSoundboard"; yield return "PnMainSoundboard"; break;
            case "VoiceChangerScrollViewer": yield return "MainVoiceChanger"; yield return "PnMainVoiceChanger"; break;
            case "VoicePresetsPanel": yield return "VoiceChangerPresets"; yield return "VoicechangerPresets"; yield return "PnVoiceChangerPresets"; break;
            case "ScenesTabRoot": yield return "MainScenes"; yield return "PnMainScenes"; break;
            case "MainSettings": yield return "MainSettings"; yield return "PnMainSettings"; break;
            case "SettingsThemesPanel": yield return "MainThemes"; yield return "ThemesPanel"; yield return "PnThemes"; yield return "PnMainThemes"; break;
            case "AboutMePanel": yield return "AboutMePanel"; yield return "PnAboutMe"; break;
            case "VBCableNoticeBorder": yield return "VBCableNoticeBorder"; yield return "SettingsVBCable"; yield return "PnVBCable"; break;
            case "VirtualMicMutedBanner": yield return "PnMuteBanner"; break;
            case "SettingsLogArea": yield return "PnSettingsLog"; break;
            case "SoundListArea": yield return "SoundboardSoundList"; yield return "PnSoundboardSoundList"; break;
            case "TimelineHost": yield return "SoundboardTimeline"; yield return "SoungboardTimeline"; yield return "PnSoundboardTimeline"; break;
            case "LoopedSceneSoundsPanel": yield return "PnScenesLoopedSounds"; break;
            case "SceneSoundsPanel": yield return "PnScenesSoundButtons"; break;

            // Buttons (Bt = button)
            case "VirtualMicMuteToggleButton": yield return "SettingsMute"; yield return "GlobalMute"; yield return "BtSettingsMute"; yield return "BtGlobalMute"; break;
            case "NextSoundButton": yield return "SoundboardNext"; yield return "BtSoundboardNext"; break;
            case "PreviousSoundButton": yield return "SoundboardPrevious"; yield return "BtSoundboardPrevious"; break;
            case "PlayPauseButton": yield return "SoundboardPlayPause"; yield return "BtSoundboardPlayPause"; break;
            case "StopSoundButton": yield return "SoundboardStop"; yield return "BtSoundboardStop"; break;
            case "SoundLoopToggleButton": yield return "SoundboardLoop"; yield return "BtSoundboardLoop"; break;
            case "InstallVBCableButton": yield return "BtInstallVBCable"; yield return "BtSettingsInstallVBCable"; break;
            case "StartEngineButton": yield return "SettingsStartEngine"; yield return "BtSettingsStartEngine"; break;
            case "StopEngineButton": yield return "SettingsStopEngine"; yield return "BtSettingsStopEngine"; break;
            case "VoiceMonitorButton": yield return "VoicechangerMonitor"; yield return "BtVoicechangerMonitor"; break;
            case "SceneApplyButton": yield return "ScenesApply"; yield return "BtScenesApply"; break;
            case "SceneDisableButton": yield return "ScenesDisable"; yield return "BtScenesDisable"; break;
            case "SceneDeleteButton": yield return "ScenesDelete"; yield return "BtScenesDelete"; break;
            case "SceneRenameButton": yield return "ScenesRename"; yield return "BtScenesRename"; break;
            case "SceneCreateNewButton": yield return "ScenesCreate"; yield return "BtScenesCreate"; break;
            case "SceneVoicePresetClearButton": yield return "BtScenesVoicePresetClear"; break;
            case "SceneVoicePresetCreateButton": yield return "BtScenesVoicePresetCreate"; break;
            case "SceneVoiceMonitorButton": yield return "ScenesVoiceMonitor"; yield return "BtScenesVoiceMonitor"; break;
            case "SceneLoopPlayLoopButton": yield return "ScenesLoopPlayLoop"; yield return "BtScenesLoopPlayLoop"; break;
            case "SceneLoopPlayOnceButton": yield return "ScenesLoopPlayOnce"; yield return "BtScenesLoopPlayOnce"; break;
            case "SceneLoopRemoveButton": yield return "ScenesLoopRemove"; yield return "BtScenesLoopRemove"; break;
            case "SceneLoopChooseButton": yield return "ScenesLoopChoose"; yield return "BtScenesLoopChoose"; break;
            case "DeleteThemeButton": yield return "BtThemeDelete"; yield return "BtSettingsThemeDelete"; break;

            // Sliders (Sl = slider). Use height/min-height/margin for visual size; WinUI Slider ignores most padding changes.
            case "SoundVirtualVolumeSlider": yield return "SoundboardVirtualMic"; yield return "SlSoundboardVirtualMic"; break;
            case "SoundMonitorVolumeSlider": yield return "SoundboardHeadphones"; yield return "SlSoundboardHeadphones"; break;
            case "SoundVirtualDelaySlider": yield return "SoundboardDelay"; yield return "SlSoundboardDelay"; break;
            case "VirtualOutputVolumeSlider": yield return "SettingsVirtualMicMaster"; yield return "SlSettingsVirtualMicMaster"; break;
            case "VoiceGainSlider": yield return "SlVoiceGain"; break;
            case "GateThresholdSlider": yield return "SlVoiceGateThreshold"; break;
            case "CompressorThresholdSlider": yield return "SlVoiceCompressorThreshold"; break;
            case "PitchSlider": yield return "SlVoicePitch"; break;
            case "FormantSlider": yield return "SlVoiceFormant"; break;
            case "BassSlider": yield return "SlVoiceBass"; break;
            case "TrebleSlider": yield return "SlVoiceTreble"; break;
            case "DistortionSlider": yield return "SlVoiceDistortion"; break;
            case "RobotSlider": yield return "SlVoiceRobot"; break;
            case "TremoloSlider": yield return "SlVoiceTremolo"; break;
            case "EchoSlider": yield return "SlVoiceEcho"; break;
            case "ReverbSlider": yield return "SlVoiceReverb"; break;
            case "RadioSlider": yield return "SlVoiceRadio"; break;
            case "BitCrusherSlider": yield return "SlVoiceBitCrusher"; break;
            case "AlienSlider": yield return "SlVoiceAlien"; break;
            case "SceneLoopHeadphonesVolumeSlider": yield return "SlScenesLoopHeadphones"; break;
            case "SceneLoopVirtualMicVolumeSlider": yield return "SlScenesLoopVirtualMic"; break;

            // Combo boxes / drop-downs (Cb = combo box)
            case "CategoryComboBox": yield return "SoundboardCategoryList"; yield return "CbSoundboardCategory"; break;
            case "InputDeviceComboBox": yield return "CbSettingsInputMicrophone"; break;
            case "MonitorOutputComboBox": yield return "CbSettingsMonitorOutput"; break;
            case "VirtualOutputComboBox": yield return "CbSettingsVirtualOutput"; break;
            case "ThemeComboBox": yield return "CbTheme"; yield return "CbSettingsTheme"; break;
            case "SceneVoicePresetComboBox": yield return "ScenesVoicePreset"; yield return "CbScenesVoicePreset"; break;
        }
    }

    private static IEnumerable<string> GetElementClasses(FrameworkElement element)
    {
        yield return "all";
        yield return "El";

        var area = DetectArea(element);
        if (!string.IsNullOrWhiteSpace(area))
        {
            yield return area;
        }

        if (element is Grid or StackPanel or ScrollViewer or VariableSizedWrapGrid)
        {
            yield return "layout";
            yield return "PnLayout";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-layout";
        }

        if (element is Border)
        {
            yield return "panel";
            yield return "Pn";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-panel";
        }

        if (element is TextBlock)
        {
            yield return "text";
            yield return "Txt";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-text";
        }

        if (element is Button)
        {
            yield return "button";
            yield return "Bt";
            yield return "primary-button";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-button";
        }

        if (element is ToggleButton)
        {
            yield return "button";
            yield return "Bt";
            yield return "toggle-button";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-button";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-toggle-button";
        }

        if (element is HyperlinkButton)
        {
            yield return "link-button";
            yield return "Bt";
            yield return "button";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-button";
        }

        if (element is ComboBox)
        {
            yield return "combo-box";
            yield return "Cb";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-combo-box";
        }

        if (element is Slider)
        {
            yield return "slider";
            yield return "Sl";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-slider";
        }

        if (element is TextBox)
        {
            yield return "text-box";
            yield return "Tx";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-text-box";
        }

        if (element is TabView)
        {
            yield return "tab-view";
            yield return "Tb";
        }

        if (element is TabViewItem)
        {
            yield return "tab-item";
            yield return "TbItem";
        }

        if (element is ListView)
        {
            yield return "list-view";
            yield return "Lv";
            if (!string.IsNullOrWhiteSpace(area)) yield return $"{area}-list-view";
        }

        var typeName = element.GetType().Name;
        if (typeName.Contains("MenuFlyout", StringComparison.OrdinalIgnoreCase) || typeName.Contains("MenuItem", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Mn";
            yield return "menu";
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
                case "border":
                    return ApplyBorderShorthand(element, value);
                case "border-color":
                    return ApplyBorderBrush(element, value);
                case "border-thickness":
                case "border-width":
                    return ApplyBorderThickness(element, value);
                case "border-style":
                    return true;
                case "corner-radius":
                case "border-radius":
                case "radius":
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
                case "width":
                    return ApplyWidth(element, value);
                case "height":
                    return ApplyHeight(element, value);
                case "min-width":
                    return ApplyMinWidth(element, value);
                case "min-height":
                    return ApplyMinHeight(element, value);
                case "max-width":
                    return ApplyMaxWidth(element, value);
                case "max-height":
                    return ApplyMaxHeight(element, value);
                case "spacing":
                case "gap":
                    return ApplySpacing(element, value);
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

    private static bool ApplyBorderShorthand(FrameworkElement element, string value)
    {
        var normalized = value.Trim();
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase) || normalized.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            var appliedNone = ApplyBorderThickness(element, "0");
            appliedNone |= ApplyBorderBrush(element, "transparent");
            return appliedNone;
        }

        var tokens = SplitCssValueTokens(normalized);
        Brush? brush = null;
        Thickness? thickness = null;

        foreach (var token in tokens)
        {
            if (IsBorderStyleToken(token))
            {
                continue;
            }

            if (brush is null && TryParseBrush(token, out var parsedBrush))
            {
                brush = parsedBrush;
                continue;
            }

            if (thickness is null && TryParseThickness(token, out var parsedThickness))
            {
                thickness = parsedThickness;
                continue;
            }
        }

        // Also allow the compact forms: border: #44FFFFFF; and border: 1;
        if (tokens.Count == 1)
        {
            if (brush is null && TryParseBrush(normalized, out var singleBrush))
            {
                brush = singleBrush;
            }
            else if (thickness is null && TryParseThickness(normalized, out var singleThickness))
            {
                thickness = singleThickness;
            }
        }

        var applied = false;
        if (brush is not null)
        {
            applied |= SetBorderBrush(element, brush);
        }
        if (thickness.HasValue)
        {
            applied |= SetBorderThickness(element, thickness.Value);
        }

        return applied;
    }

    private static bool IsBorderStyleToken(string token)
    {
        return token.Trim().ToLowerInvariant() switch
        {
            "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge" or "inset" or "outset" or "hidden" => true,
            _ => false
        };
    }

    private static bool SetBorderBrush(FrameworkElement element, Brush brush)
    {
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

    private static bool SetBorderThickness(FrameworkElement element, Thickness thickness)
    {
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

    private static bool ApplyBorderBrush(FrameworkElement element, string value)
    {
        if (!TryParseBrush(value, out var brush)) return false;
        return SetBorderBrush(element, brush);
    }

    private static bool ApplyBorderThickness(FrameworkElement element, string value)
    {
        if (!TryParseThickness(value, out var thickness)) return false;
        return SetBorderThickness(element, thickness);
    }

    private static bool ApplyCornerRadius(FrameworkElement element, string value)
    {
        if (!TryParseCornerRadius(value, out var radius)) return false;
        if (element is Border border)
        {
            border.CornerRadius = radius;
            return true;
        }

        // Many WinUI controls such as Button/ToggleButton/ComboBox expose CornerRadius,
        // but not all of them share a compile-time interface. Reflection keeps the theme
        // engine safe and lets border-radius work for controls where WinUI supports it.
        return TrySetCornerRadiusProperty(element, radius);
    }

    private static bool TrySetCornerRadiusProperty(FrameworkElement element, CornerRadius radius)
    {
        var property = element.GetType().GetProperty("CornerRadius");
        if (property is null || !property.CanWrite || property.PropertyType != typeof(CornerRadius))
        {
            return false;
        }

        property.SetValue(element, radius);
        return true;
    }

    private static bool TryGetCornerRadiusProperty(FrameworkElement element, out CornerRadius radius)
    {
        var property = element.GetType().GetProperty("CornerRadius");
        if (property is null || !property.CanRead || property.PropertyType != typeof(CornerRadius))
        {
            radius = default;
            return false;
        }

        var value = property.GetValue(element);
        if (value is CornerRadius cornerRadius)
        {
            radius = cornerRadius;
            return true;
        }

        radius = default;
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


    private static bool ApplyWidth(FrameworkElement element, string value)
    {
        if (!TryParseDouble(value, out var width) || width < 0) return false;
        element.Width = width;
        return true;
    }

    private static bool ApplyHeight(FrameworkElement element, string value)
    {
        if (!TryParseDouble(value, out var height) || height < 0) return false;
        element.Height = height;
        return true;
    }

    private static bool ApplyMinWidth(FrameworkElement element, string value)
    {
        if (!TryParseDouble(value, out var width) || width < 0) return false;
        element.MinWidth = width;
        return true;
    }

    private static bool ApplyMinHeight(FrameworkElement element, string value)
    {
        if (!TryParseDouble(value, out var height) || height < 0) return false;
        element.MinHeight = height;
        return true;
    }

    private static bool ApplyMaxWidth(FrameworkElement element, string value)
    {
        if (!TryParseDouble(value, out var width) || width < 0) return false;
        element.MaxWidth = width;
        return true;
    }

    private static bool ApplyMaxHeight(FrameworkElement element, string value)
    {
        if (!TryParseDouble(value, out var height) || height < 0) return false;
        element.MaxHeight = height;
        return true;
    }

    private static bool ApplySpacing(FrameworkElement element, string value)
    {
        if (!TryParseDouble(value, out var spacing) || spacing < 0) return false;
        switch (element)
        {
            case StackPanel stackPanel:
                stackPanel.Spacing = spacing;
                return true;
            case Grid grid:
                grid.ColumnSpacing = spacing;
                grid.RowSpacing = spacing;
                return true;
            default:
                return false;
        }
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

    private static List<string> SplitCssValueTokens(string value)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (i > start)
                {
                    var token = value[start..i].Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        result.Add(token);
                    }
                }
                start = i + 1;
            }
        }

        if (start < value.Length)
        {
            var token = value[start..].Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                result.Add(token);
            }
        }

        return result;
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
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && TryParseDouble(parts[0], out var all))
        {
            radius = new CornerRadius(all);
            return true;
        }

        if (parts.Length == 2
            && TryParseDouble(parts[0], out var topBottom)
            && TryParseDouble(parts[1], out var leftRight))
        {
            radius = new CornerRadius(leftRight, topBottom, leftRight, topBottom);
            return true;
        }

        if (parts.Length == 4
            && TryParseDouble(parts[0], out var topLeft)
            && TryParseDouble(parts[1], out var topRight)
            && TryParseDouble(parts[2], out var bottomRight)
            && TryParseDouble(parts[3], out var bottomLeft))
        {
            radius = new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);
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

  Naming guide:
    Pn = panel/container: #PnMainHeader, #PnAboutMe, #PnVBCable, .Pn
    Bt = button/toggle/link button: #BtSettingsMute, #BtSoundboardNext, .Bt
    Sl = slider: #SlSettingsVirtualMicMaster, #SlSoundboardVirtualMic, .Sl
    Cb = combo box/drop-down: #CbTheme, #CbSettingsInputMicrophone, .Cb
    Txt = text: .Txt
    Tb = tabs: .Tb, .TbItem
    Mn = context/menu flyout elements where available: .Mn

  The new file is intentionally non-destructive: all example declarations are commented.
  Uncomment what you want. Saving this file reloads the theme live.

  Supported properties include:
    background, foreground/color, border, border-color, border-thickness/border-width, border-radius/corner-radius/radius,
    opacity, font-size, font-weight, padding, margin, width, height, min-width, min-height, max-width, max-height, spacing/gap.

  Border shorthand examples:
    border: solid var(--panel-border) 1;
    border: dashed #66FFFFFF 2;
    border: none;
*/

:root {
  --app-background: #000000;
  --titlebar-background: #000000;
  --panel-background: #10151D;
  --panel-border: #44546A82;
  --text-primary: #F3F7FF;
  --text-secondary: #B6C0D0;
  --accent: #7DD3FC;
  --danger: #FF5D5D;
  --warning: #F59E0B;
  --success: #37D67A;
  --button-background: #182232;
  --button-hover: #223149;
  --button-pressed: #2B405F;
  --button-border: #52647D;
  --radius-panel: 18;
  --radius-button: 12;
  --radius-input: 14;
}

/* Global */
#RootGrid { /* background: var(--app-background); */ }
.Pn { /* background: var(--panel-background); border: solid var(--panel-border) 1; border-radius: var(--radius-panel); */ }
.Bt { /* background: var(--button-background); foreground: var(--text-primary); border: solid var(--button-border) 1; border-radius: var(--radius-button); padding: 14 7; */ }
.Bt:hover { /* background: var(--button-hover); */ }
.Bt:pressed { /* background: var(--button-pressed); */ }
.Bt:on { /* background: var(--accent); foreground: #001018; */ }
.Cb { /* background: #0D1420; foreground: var(--text-primary); border: solid var(--button-border) 1; border-radius: var(--radius-input); padding: 12 6; */ }
.Sl { /* foreground: var(--accent); height: 32; min-height: 32; margin: 0 4 0 4; */ }
.Txt { /* foreground: var(--text-primary); */ }
.link-button { /* foreground: var(--accent); */ }

/* Main panels */
#PnMainHeader { /* background: linear-gradient(90deg, #101827, #071018); border-radius: 18; padding: 8; */ }
#PnMainTabs { /* background: #08111B; border-color: #314155; border-thickness: 1; border-radius: 18; padding: 6; */ }
#PnMainSoundboard { /* background: transparent; */ }
#PnMainVoiceChanger { /* background: transparent; */ }
#PnMainScenes { /* background: transparent; */ }
#PnMainSettings { /* background: transparent; */ }
#PnThemes { /* background: var(--panel-background); border-color: var(--panel-border); border-radius: var(--radius-panel); */ }
#PnAboutMe { /* background: var(--panel-background); border-color: var(--panel-border); border-radius: var(--radius-panel); */ }
#PnVBCable { /* background: #111F1B; border-color: #3369D38B; border-radius: var(--radius-panel); */ }

/* Header / mute */
#BtSettingsMute, #BtGlobalMute { /* border-radius: 14; padding: 16 6; */ }
#VirtualMicMuteStatusTextBlock { /* foreground: var(--success); */ }
#PnMuteBanner { /* background: #44220000; border-color: #88FF4D4D; border-radius: 12; */ }

/* SoundBoard */
#PnSoundboardTimeline { /* background: linear-gradient(90deg, #0B1624, #0F243A); border-radius: 16; */ }
#PnSoundboardSoundList { /* background: #080D14; border-radius: 16; */ }
#BtSoundboardNext, #BtSoundboardPrevious, #BtSoundboardPlayPause, #BtSoundboardStop, #BtSoundboardLoop { /* border-radius: var(--radius-button); */ }
#SlSoundboardVirtualMic, #SlSoundboardHeadphones, #SlSoundboardDelay { /* height: 34; min-height: 34; margin: 4 0 4 0; */ }
.soundboard-sound { /* background: transparent; border-radius: 10; */ }
.soundboard-sound:hover { /* background: #18FFFFFF; */ }

/* Voice Changer */
#PnVoiceChangerPresets { /* background: #080D14; border-radius: 16; */ }
#BtVoicechangerMonitor { /* border-radius: var(--radius-button); */ }
.voicechanger-slider, #SlVoiceGain, #SlVoicePitch, #SlVoiceFormant { /* height: 32; min-height: 32; */ }

/* Scenes */
#BtScenesApply:pressed { /* background: var(--success); */ }
#BtScenesDisable:pressed, #BtScenesDelete:pressed { /* background: var(--danger); */ }
#SlScenesLoopHeadphones, #SlScenesLoopVirtualMic { /* height: 30; min-height: 30; */ }

/* Settings */
#SlSettingsVirtualMicMaster { /* height: 36; min-height: 36; margin: 8 0 8 0; */ }
#BtSettingsStartEngine:pressed { /* background: var(--success); */ }
#BtSettingsStopEngine:pressed { /* background: var(--danger); */ }
#CbSettingsInputMicrophone, #CbSettingsMonitorOutput, #CbTheme { /* border-radius: var(--radius-input); */ }
#BtThemeDelete { /* border-color: #66505050; */ }

/* Notes:
   Padding on Slider is limited by WinUI's internal Slider template.
   To make a slider easier to hit or visually larger, use height/min-height/margin:
   #SlSettingsVirtualMicMaster { height: 40; min-height: 40; margin: 8 0; }
*/
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
        private double Width { get; set; }
        private double Height { get; set; }
        private double MinWidth { get; set; }
        private double MinHeight { get; set; }
        private double MaxWidth { get; set; }
        private double MaxHeight { get; set; }
        private double Opacity { get; set; }
        private double? FontSize { get; set; }
        private Windows.UI.Text.FontWeight? FontWeight { get; set; }

        public static ElementStyleSnapshot Capture(FrameworkElement element)
        {
            var snapshot = new ElementStyleSnapshot
            {
                Margin = element.Margin,
                Width = element.Width,
                Height = element.Height,
                MinWidth = element.MinWidth,
                MinHeight = element.MinHeight,
                MaxWidth = element.MaxWidth,
                MaxHeight = element.MaxHeight,
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
                    if (TryGetCornerRadiusProperty(control, out var controlCornerRadius)) snapshot.CornerRadius = controlCornerRadius;
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
            element.Width = Width;
            element.Height = Height;
            element.MinWidth = MinWidth;
            element.MinHeight = MinHeight;
            element.MaxWidth = MaxWidth;
            element.MaxHeight = MaxHeight;
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
                    if (CornerRadius.HasValue) TrySetCornerRadiusProperty(control, CornerRadius.Value);
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
