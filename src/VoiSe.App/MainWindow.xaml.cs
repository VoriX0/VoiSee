using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using XamlLine = Microsoft.UI.Xaml.Shapes.Line;
using XamlRectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VoiSe.Audio;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace VoiSe.App;

public sealed partial class MainWindow : Window
{
    private readonly AudioDeviceCatalog _catalog = new();
    private readonly DispatcherTimer _routeRestartTimer;
    private readonly DispatcherTimer _timelineTimer;
    private readonly SettingsStore _settingsStore = new();
    private readonly SoundBoardLibraryStore _libraryStore;
    private readonly VoicePresetStore _voicePresetStore;
    private readonly SceneStore _sceneStore;
    private readonly ThemeManager _themeManager;
    private readonly DispatcherTimer _themeReloadTimer;
    private VoiSeUserSettings _settings;
    private SoundBoardLibrary _library;
    private IReadOnlyList<VoicePreset> _voicePresets = Array.Empty<VoicePreset>();
    private IReadOnlyList<VoiSeScene> _scenes = Array.Empty<VoiSeScene>();
    private VoiSeScene? _selectedScene;
    private string? _activeSceneId;
    private string? _lastAppliedVoicePresetName;
    private VoicePreset? _voicePresetBeforeActiveScene;
    private string? _lastAppliedVoicePresetNameBeforeActiveScene;
    private bool _loadingVoicePreset;
    private bool _syncingVoiceControls;
    private readonly StringBuilder _logBuffer = new();
    private readonly DispatcherTimer _voiceSettingsApplyTimer;
    private Gate2UnifiedAudioEngine? _engine;
    private string? _soundFilePath;
    private SoundBoardSound? _selectedSound;
    private bool _loadingLibrary;
    private bool _refreshingDevices;
    private bool _manualStopRequested;
    private string _pendingRestartReason = "settings changed";
    private bool _voiceMonitorEnabled;
    private bool _virtualMicMuted;
    private bool _loadingSettings = true;
    private bool _loadedOnce;
    private bool _timelineUserDragging;
    private double _timelineMaximumSeconds = 1.0;
    private string _trackSearchText = string.Empty;
    private List<SoundBoardSound> _visibleSounds = new();
    private string? _lastSoundRowClickSoundId;
    private string? _currentSoundDisplayName;
    private DateTime _lastSoundRowClickUtc = DateTime.MinValue;
    private LowLevelMouseProc? _lowLevelMouseProc;
    private LowLevelKeyboardProc? _lowLevelKeyboardProc;
    private IntPtr _mouseHookHandle;
    private IntPtr _keyboardHookHandle;
    private IntPtr _windowHandle;
    private VoicePreset? _pushToTalkPreviousVoicePreset;
    private HotkeyGesture? _activePushToTalkGesture;
    private bool _capturingHotkey;
    private bool _loadingThemeChoices;
    private FileSystemWatcher? _themeFileWatcher;
    private string? _watchedThemePath;
    private int _themeReapplyGeneration;
    private VoiSeeXamlTheme? _activeTheme;
    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;
    private const int WmMouseWheel = 0x020A;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const double SoundWheelZoneExpandUpRatio = 0.25;
    private const double SoundWheelZoneExpandRightRatio = 2.00;
    private const double SoundWheelZoneExpandBottomRatio = 1.60;
    private const double SceneListWheelZoneExpandDownRatio = 0.65;
    private const double ModalWheelZoneExpandLeftRatio = 0.50;
    private const double ModalWheelZoneExpandRightRatio = 0.50;
    private const double ModalWheelZoneExpandBottomRatio = 1.00;
    private const double SettingsWheelZoneExpandRightRatio = 0.50;
    private const double SceneSoundButtonsWheelZoneExpandRightRatio = 0.50;
    private const double SceneListWheelZoneExpandRightRatio = 0.15;
    private const double IconPickerWheelZoneShiftRightRatio = 0.15;
    private const double VoiceValueMin = -9999.0;
    private const double VoiceValueMax = 9999.0;
    private const double SceneSoundButtonWidth = 252.0;
    private const double SceneSoundButtonHeight = 112.0;
    private const double SceneLoopIconHeight = 42.0;
    private const string DefaultVoicePresetIcon = "\uE720";
    private const double SoundBoardWheelPixelsPerNotch = 56.0;
    private const string VBCableDownloadUrl = "https://vb-audio.com/Cable/";
    private const string MuteOnCueRelativePath = "Assets\\Audio\\mute_on.wav";
    private const string MuteOffCueRelativePath = "Assets\\Audio\\mute_off.wav";
    private const string SoundEditorPreviewPlaybackKey = "__voisee_sound_editor_preview";
    private bool _suppressSoundBoardTimelineForEditorPreview;
    private bool _soundEditorActive;
    private ScrollViewer? _activeSoundEditorScrollViewer;
    private Action? _soundEditorPlayPauseAction;
    private Action? _soundEditorStopAction;
    private readonly Dictionary<string, SceneTimelineBinding> _sceneTimelineBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _soundDurationSecondsCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadingSceneUi;
    private bool _soundBoardLoopEnabled;
    private string? _lastSoundBoardDropSignature;
    private DateTime _lastSoundBoardDropUtc = DateTime.MinValue;
    private bool _suppressMainTabWheelRouting;
    private ScrollViewer? _activeIconPickerScrollViewer;
    private FrameworkElement? _activeIconPickerWheelZoneElement;
    private double _activeModalWheelZoneLeftExtensionRatio;
    private double _activeModalWheelZoneRightExtensionRatio;
    private double _activeModalWheelZoneHorizontalShiftRatio;

    public MainWindow()
    {
        StartupLog.Write("MainWindow constructor started.");
        _settings = _settingsStore.Load();
        _libraryStore = new SoundBoardLibraryStore(_settingsStore.DataDirectory);
        _voicePresetStore = new VoicePresetStore(_settingsStore.DataDirectory);
        _sceneStore = new SceneStore(_settingsStore.DataDirectory);
        _themeManager = new ThemeManager(_settingsStore.DataDirectory);
        _library = _libraryStore.Load();
        InitializeComponent();
        _windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureTitleBar();

        _themeReloadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _themeReloadTimer.Tick += OnThemeReloadTimerTick;
        InitializeThemeSystem();
        MainTabView.SelectionChanged += OnMainTabSelectionChanged;
        SoundInputOverlay.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnSoundInputOverlayPointerWheelChanged), true);
        InstallSoundBoardWheelHook();
        InstallGlobalKeyboardHook();
        Closed += OnClosed;
        Activated += OnActivated;

        _routeRestartTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _routeRestartTimer.Tick += OnRouteRestartTimerTick;

        _voiceSettingsApplyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _voiceSettingsApplyTimer.Tick += OnVoiceSettingsApplyTimerTick;

        _timelineTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timelineTimer.Tick += OnTimelineTimerTick;
        _timelineTimer.Start();

        AppendLog("VoiSee Version 10.1.2 UI started.");
        AppendLog($"Settings path: {_settingsStore.SettingsPath}");
        StartupLog.Write("MainWindow initialized; waiting for first activation.");
    }


    private void ConfigureTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);

            var titleBar = AppWindow.TitleBar;
            var black = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x00, 0x00, 0x00);
            var darkHover = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x20, 0x20, 0x20);
            var darkPressed = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x10, 0x10, 0x10);
            var white = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            var muted = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xDD, 0xDD, 0xDD);

            titleBar.BackgroundColor = black;
            titleBar.ForegroundColor = white;
            titleBar.InactiveBackgroundColor = black;
            titleBar.InactiveForegroundColor = muted;
            titleBar.ButtonBackgroundColor = black;
            titleBar.ButtonForegroundColor = white;
            titleBar.ButtonHoverBackgroundColor = darkHover;
            titleBar.ButtonHoverForegroundColor = white;
            titleBar.ButtonPressedBackgroundColor = darkPressed;
            titleBar.ButtonPressedForegroundColor = white;
            titleBar.ButtonInactiveBackgroundColor = black;
            titleBar.ButtonInactiveForegroundColor = muted;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Title bar color setup skipped: {ex.Message}");
        }
    }



    private void InitializeThemeSystem()
    {
        try
        {
            _themeManager.EnsureThemesDirectory();
            var migration = _themeManager.MigrateLegacyCssThemes(_settings.ThemeFilePath);
            if (migration.ActiveThemeWasLegacyCss)
            {
                _settings.ThemeFilePath = null;
                _settingsStore.Save(_settings);
            }

            PopulateThemeComboBox();
            ApplyThemeFromSettings(log: false);
            WatchActiveThemeFile();

            if (migration.MovedFileCount > 0)
            {
                AppendLog($"Legacy CSS themes archived: {migration.MovedFileCount}; folder: {migration.LegacyDirectory}");
                RootGrid.Loaded += async (_, _) =>
                {
                    await ShowMessageDialogAsync(
                        "Themes migrated",
                        $"VoiSee 10 now uses native WinUI XAML themes. {migration.MovedFileCount} old CSS theme file(s) were moved to:\n{migration.LegacyDirectory}\n\nThe active theme was reset to Default Dark. CSS themes are no longer loaded at runtime.");
                };
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Theme system init skipped: {ex.Message}");
        }
    }

    private void PopulateThemeComboBox()
    {
        if (ThemeComboBox is null)
        {
            return;
        }

        _loadingThemeChoices = true;
        try
        {
            ThemeComboBox.DisplayMemberPath = nameof(ThemeComboItem.DisplayName);
            ThemeComboBox.Items.Clear();
            ThemeComboBox.Items.Add(new ThemeComboItem("Default Dark", null, true));

            foreach (var path in _themeManager.GetThemeFiles())
            {
                var name = ThemeManager.GetDisplayName(path);
                ThemeComboBox.Items.Add(new ThemeComboItem(name, path, false));
            }

            SelectThemeComboItem(_settings.ThemeFilePath);
        }
        finally
        {
            _loadingThemeChoices = false;
            UpdateThemeControls();
        }
    }

    private void UpdateThemeControls()
    {
        var userThemeSelected = ThemeComboBox?.SelectedItem is ThemeComboItem item
            && !item.IsDefault
            && !string.IsNullOrWhiteSpace(item.FilePath)
            && File.Exists(item.FilePath);

        if (OpenThemeFileButton is not null)
        {
            OpenThemeFileButton.IsEnabled = userThemeSelected;
        }

        if (RenameThemeButton is not null)
        {
            RenameThemeButton.IsEnabled = userThemeSelected;
        }

        if (DeleteThemeButton is not null)
        {
            DeleteThemeButton.IsEnabled = userThemeSelected;
        }
    }

    private void SelectThemeComboItem(string? themePath)
    {
        if (ThemeComboBox is null)
        {
            return;
        }

        var normalized = string.IsNullOrWhiteSpace(themePath) ? null : Path.GetFullPath(themePath);
        foreach (var item in ThemeComboBox.Items.OfType<ThemeComboItem>())
        {
            if ((normalized is null && item.IsDefault) ||
                (normalized is not null && item.FilePath is not null && Path.GetFullPath(item.FilePath).Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                ThemeComboBox.SelectedItem = item;
                return;
            }
        }

        ThemeComboBox.SelectedIndex = 0;
    }

    private void ApplyThemeFromSettings(bool log = true)
    {
        var previousTheme = _activeTheme ?? VoiSeeXamlTheme.DefaultDark;
        try
        {
            if (!string.IsNullOrWhiteSpace(_settings.ThemeFilePath) && !File.Exists(_settings.ThemeFilePath))
            {
                _settings.ThemeFilePath = null;
                _settingsStore.Save(_settings);
                PopulateThemeComboBox();
                WatchActiveThemeFile();
            }

            var theme = _themeManager.LoadTheme(_settings.ThemeFilePath);
            var resourceCount = _themeManager.ApplyTheme(RootGrid, theme);
            _activeTheme = theme;
            ApplyTitleBarTheme();
            if (ThemeStatusTextBlock is not null)
            {
                ThemeStatusTextBlock.Text = string.IsNullOrWhiteSpace(theme.SourcePath)
                    ? "Theme: Default Dark"
                    : $"Theme: {theme.Name} ({theme.SourcePath})";
            }

            if (log)
            {
                AppendLog($"Native XAML theme applied: {theme.Name}; resources: {resourceCount}.");
            }
        }
        catch (Exception ex)
        {
            // LoadTheme validates the candidate before ThemeManager replaces the
            // active dictionary, so the previous working theme is still visible.
            _activeTheme = previousTheme;
            _settings.ThemeFilePath = previousTheme.SourcePath;
            _settingsStore.Save(_settings);
            SelectThemeComboItem(previousTheme.SourcePath);
            WatchActiveThemeFile();

            if (ThemeStatusTextBlock is not null)
            {
                ThemeStatusTextBlock.Text = $"Theme error — kept {previousTheme.Name}: {ex.Message}";
            }
            AppendLog($"XAML theme apply error; previous theme kept: {ex.Message}");
        }
    }

    private void ApplyTitleBarTheme()
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            var background = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x00, 0x00, 0x00);
            var foreground = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            var hover = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x20, 0x20, 0x20);
            var pressed = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x10, 0x10, 0x10);

            if (_themeManager.TryGetColor("VoiSee.TitleBarBackgroundBrush", out var themedBackground)) background = themedBackground;
            if (_themeManager.TryGetColor("VoiSee.TitleBarForegroundBrush", out var themedForeground)) foreground = themedForeground;
            if (_themeManager.TryGetColor("VoiSee.TitleBarHoverBrush", out var themedHover)) hover = themedHover;
            if (_themeManager.TryGetColor("VoiSee.TitleBarPressedBrush", out var themedPressed)) pressed = themedPressed;

            titleBar.BackgroundColor = background;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveBackgroundColor = background;
            titleBar.InactiveForegroundColor = foreground;
            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = pressed;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonInactiveBackgroundColor = background;
            titleBar.ButtonInactiveForegroundColor = foreground;
        }
        catch (Exception ex)
        {
            AppendLog($"Theme title bar apply skipped: {ex.Message}");
        }
    }

    private void WatchActiveThemeFile()
    {
        _themeFileWatcher?.Dispose();
        _themeFileWatcher = null;
        _watchedThemePath = null;

        var path = _settings.ThemeFilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            _watchedThemePath = Path.GetFullPath(path);
            _themeFileWatcher = new FileSystemWatcher(Path.GetDirectoryName(_watchedThemePath)!, Path.GetFileName(_watchedThemePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _themeFileWatcher.Changed += OnThemeFileChanged;
            _themeFileWatcher.Created += OnThemeFileChanged;
            _themeFileWatcher.Renamed += OnThemeFileChanged;
            _themeFileWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            AppendLog($"Theme live reload watcher error: {ex.Message}");
        }
    }

    private void OnThemeFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_watchedThemePath is null)
        {
            return;
        }

        var changedPath = e is RenamedEventArgs renamed ? renamed.FullPath : e.FullPath;
        if (!Path.GetFullPath(changedPath).Equals(_watchedThemePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            _themeReloadTimer.Stop();
            _themeReloadTimer.Start();
        });
    }

    private void OnThemeReloadTimerTick(object? sender, object e)
    {
        _themeReloadTimer.Stop();
        ApplyThemeFromSettings();
    }


    private void ReapplyThemeAfterVisualTreeChange()
    {
        // Native WinUI resources are resolved by each control when its template
        // is materialized. Unlike the removed CSS engine, tab switching needs no
        // visual-tree traversal or theme repaint.
    }

    private void OnMainTabViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ReapplyThemeAfterVisualTreeChange();
    }

    private void OnThemeComboBoxDropDownOpened(object sender, object e)
    {
        PopulateThemeComboBox();
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingThemeChoices)
        {
            return;
        }

        if (ThemeComboBox.SelectedItem is not ThemeComboItem item)
        {
            return;
        }

        UpdateThemeControls();

        _settings.ThemeFilePath = item.IsDefault ? null : item.FilePath;
        _settingsStore.Save(_settings);
        ApplyThemeFromSettings();
        WatchActiveThemeFile();
    }

    private async void OnCreateNewThemeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _themeManager.CreateNewThemeFile();
            _settings.ThemeFilePath = path;
            _settingsStore.Save(_settings);
            PopulateThemeComboBox();
            ApplyThemeFromSettings();
            WatchActiveThemeFile();
            OpenFileWithShell(path);
            AppendLog($"Theme created: {path}");
        }
        catch (Exception ex)
        {
            AppendLog($"Create theme error: {ex.Message}");
            await ShowMessageDialogAsync("Theme error", ex.Message);
        }
    }

    private async void OnOpenThemeFileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ThemeComboBox?.SelectedItem is not ThemeComboItem item ||
                item.IsDefault ||
                string.IsNullOrWhiteSpace(item.FilePath))
            {
                await ShowMessageDialogAsync(
                    "Open theme file",
                    "Select a user theme first. Default Dark is built into VoiSee and cannot be opened or edited directly.");
                UpdateThemeControls();
                return;
            }

            var path = item.FilePath;
            if (!File.Exists(path))
            {
                await ShowMessageDialogAsync(
                    "Open theme file",
                    "The selected theme file no longer exists. The theme list will be refreshed.");
                PopulateThemeComboBox();
                return;
            }

            OpenFileWithShell(path);
            AppendLog($"Theme file opened: {path}");
        }
        catch (Exception ex)
        {
            AppendLog($"Open theme file error: {ex.Message}");
            await ShowMessageDialogAsync("Theme error", ex.Message);
        }
    }

    private async void OnOpenThemeFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _themeManager.EnsureThemesDirectory();
            OpenFolderWithShell(_themeManager.ThemesDirectory);
            AppendLog($"Theme folder opened: {_themeManager.ThemesDirectory}");
        }
        catch (Exception ex)
        {
            AppendLog($"Open theme folder error: {ex.Message}");
            await ShowMessageDialogAsync("Theme error", ex.Message);
        }
    }

    private async void OnRenameThemeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ThemeComboBox?.SelectedItem is not ThemeComboItem item || item.IsDefault || string.IsNullOrWhiteSpace(item.FilePath))
            {
                await ShowMessageDialogAsync("Rename theme", "Select a user theme first. The built-in Default Dark theme cannot be renamed.");
                return;
            }

            var oldPath = item.FilePath;
            if (!File.Exists(oldPath))
            {
                await ShowMessageDialogAsync("Rename theme", "The selected theme file does not exist anymore. The theme list will be refreshed.");
                PopulateThemeComboBox();
                return;
            }

            var fullOldPath = Path.GetFullPath(oldPath);
            var themesDirectory = Path.GetFullPath(_themeManager.ThemesDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullOldPath.StartsWith(themesDirectory, StringComparison.OrdinalIgnoreCase))
            {
                await ShowMessageDialogAsync("Rename theme", "Only themes from the VoiSee themes folder can be renamed here.");
                return;
            }

            var currentName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(oldPath));
            var nameBox = new TextBox
            {
                Text = currentName,
                PlaceholderText = "Theme name",
                MinWidth = 320,
                SelectionStart = 0,
                SelectionLength = currentName.Length
            };

            var dialog = new ContentDialog
            {
                Title = "Rename theme",
                Content = nameBox,
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var requestedName = SanitizeThemeFileName(nameBox.Text);
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                await ShowMessageDialogAsync("Rename theme", "Enter a theme name.");
                return;
            }

            var newPath = Path.Combine(_themeManager.ThemesDirectory, requestedName + ThemeManager.ThemeExtension);
            if (Path.GetFullPath(newPath).Equals(fullOldPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(newPath))
            {
                await ShowMessageDialogAsync("Rename theme", $"A theme named '{requestedName}' already exists.");
                return;
            }

            var activeThemePath = _settings.ThemeFilePath;
            var wasActive = !string.IsNullOrWhiteSpace(activeThemePath)
                && Path.GetFullPath(activeThemePath).Equals(fullOldPath, StringComparison.OrdinalIgnoreCase);

            if (wasActive)
            {
                _themeFileWatcher?.Dispose();
                _themeFileWatcher = null;
                _watchedThemePath = null;
            }

            File.Move(fullOldPath, newPath);

            if (wasActive)
            {
                _settings.ThemeFilePath = newPath;
                _settingsStore.Save(_settings);
            }

            PopulateThemeComboBox();
            SelectThemeComboItem(wasActive ? newPath : _settings.ThemeFilePath);
            ApplyThemeFromSettings();
            WatchActiveThemeFile();
            AppendLog($"Theme renamed: {fullOldPath} -> {newPath}");
        }
        catch (Exception ex)
        {
            AppendLog($"Rename theme error: {ex.Message}");
            await ShowMessageDialogAsync("Theme error", ex.Message);
        }
    }

    private static string SanitizeThemeFileName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.EndsWith(ThemeManager.ThemeExtension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^ThemeManager.ThemeExtension.Length];
        }
        else if (name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".xaml".Length];
        }

        name = name.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        while (name.Contains("..", StringComparison.Ordinal))
        {
            name = name.Replace("..", ".", StringComparison.Ordinal);
        }

        return name.Trim(' ', '.');
    }

    private async void OnDeleteThemeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ThemeComboBox?.SelectedItem is not ThemeComboItem item || item.IsDefault || string.IsNullOrWhiteSpace(item.FilePath))
            {
                await ShowMessageDialogAsync("Delete theme", "Select a user theme first. The built-in Default Dark theme cannot be deleted.");
                return;
            }

            var path = item.FilePath;
            if (!File.Exists(path))
            {
                await ShowMessageDialogAsync("Delete theme", "The selected theme file does not exist anymore. The theme list will be refreshed.");
                PopulateThemeComboBox();
                return;
            }

            var fullPath = Path.GetFullPath(path);
            var themesDirectory = Path.GetFullPath(_themeManager.ThemesDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(themesDirectory, StringComparison.OrdinalIgnoreCase))
            {
                await ShowMessageDialogAsync("Delete theme", "Only themes from the VoiSee themes folder can be deleted here.");
                return;
            }

            var fileName = Path.GetFileName(path);
            var dialog = new ContentDialog
            {
                Title = "Delete theme?",
                Content = $"Delete '{fileName}'?\n\nThis will remove the .voiseetheme.xaml file from the VoiSee themes folder.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var activeThemePath = _settings.ThemeFilePath;
            var wasActive = !string.IsNullOrWhiteSpace(activeThemePath)
                && Path.GetFullPath(activeThemePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase);

            if (wasActive)
            {
                _settings.ThemeFilePath = null;
                _settingsStore.Save(_settings);
                _themeFileWatcher?.Dispose();
                _themeFileWatcher = null;
                _watchedThemePath = null;
            }

            File.Delete(fullPath);
            PopulateThemeComboBox();
            ApplyThemeFromSettings();
            WatchActiveThemeFile();
            AppendLog($"Theme deleted: {fullPath}");
        }
        catch (Exception ex)
        {
            AppendLog($"Delete theme error: {ex.Message}");
            await ShowMessageDialogAsync("Theme error", ex.Message);
        }
    }

    private async void OnImportThemeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".xaml");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var path = _themeManager.ImportTheme(file.Path);
            _settings.ThemeFilePath = path;
            _settingsStore.Save(_settings);
            PopulateThemeComboBox();
            ApplyThemeFromSettings();
            WatchActiveThemeFile();
            AppendLog($"Theme imported: {path}");
        }
        catch (Exception ex)
        {
            AppendLog($"Import theme error: {ex.Message}");
            await ShowMessageDialogAsync("Theme error", ex.Message);
        }
    }

    private async void OnExportThemeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = string.IsNullOrWhiteSpace(_settings.ThemeFilePath)
                    ? "DefaultDark.voiseetheme.xaml"
                    : Path.GetFileName(_settings.ThemeFilePath)
            };
            picker.FileTypeChoices.Add("VoiSee XAML theme", new List<string> { ".xaml" });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            _themeManager.ExportTheme(_settings.ThemeFilePath, file.Path);
            AppendLog($"Theme exported: {file.Path}");
        }
        catch (Exception ex)
        {
            AppendLog($"Export theme error: {ex.Message}");
            await ShowMessageDialogAsync("Theme error", ex.Message);
        }
    }

    private void OnResetThemeClick(object sender, RoutedEventArgs e)
    {
        _settings.ThemeFilePath = null;
        _settingsStore.Save(_settings);
        PopulateThemeComboBox();
        ApplyThemeFromSettings();
        WatchActiveThemeFile();
        AppendLog("Theme reset to Default Dark.");
    }

    private static void OpenFileWithShell(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }


    private static void OpenFolderWithShell(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        RestoreSettingsAfterWindowActivation();
    }

    private void RestoreSettingsAfterWindowActivation()
    {
        _loadingSettings = true;
        try
        {
            AppendLog("Restoring saved settings...");
            StartupLog.Write("Gate 7.1 restore started.");

            ApplyStoredScalarSettingsToControls();
            AppendLog("Saved scalar settings applied.");
            StartupLog.Write("Gate 7.1 scalar settings applied.");

            RefreshDevices(saveAfterRefresh: false);
            LoadSoundBoardLibraryIntoUi();
            LoadVoicePresetsIntoUi();
            LoadScenesIntoUi();
            AppendLog("Settings restored.");
            StartupLog.Write("Gate 7.1 restore completed.");
        }
        catch (Exception ex)
        {
            StartupLog.Write("Gate 7.1 restore error: " + ex);
            AppendLog($"Settings restore error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _loadingSettings = false;
            UpdateAllLabels();
            UpdateTransportHotkeySummary();
            UpdateVirtualMicMuteUi();
            AutoStartEngineAfterRestore();
        }
    }


    private void AutoStartEngineAfterRestore()
    {
        if (_engine is not null)
        {
            return;
        }

        _manualStopRequested = false;
        if (StartEngine(logAlreadyRunning: false))
        {
            AppendLog("Engine auto-started on application launch.");
        }
        else
        {
            AppendLog("Engine auto-start skipped. Check selected audio devices in Settings, then use manual Start Engine if needed.");
        }
    }

    private static bool IsLikelyVBCableDevice(AudioDeviceInfo? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.FriendlyName))
        {
            return false;
        }

        var name = device.FriendlyName;
        return name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VB-CABLE", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VB Audio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);
    }

    private static AudioDeviceInfo? FindDetectedVBCableRenderDevice(IEnumerable<AudioDeviceInfo> renderDevices)
    {
        return renderDevices.FirstOrDefault(IsLikelyVBCableDevice);
    }

    private bool IsVBCableReady()
    {
        return IsLikelyVBCableDevice(VirtualOutputComboBox?.SelectedItem as AudioDeviceInfo);
    }

    private void UpdateVBCableUiState()
    {
        var virtualOutput = VirtualOutputComboBox?.SelectedItem as AudioDeviceInfo;
        var hasCable = IsLikelyVBCableDevice(virtualOutput);

        if (VBCableNoticeBorder is not null)
        {
            VBCableNoticeBorder.Visibility = Visibility.Visible;
            VBCableNoticeBorder.BorderBrush = new SolidColorBrush(hasCable
                ? Microsoft.UI.ColorHelper.FromArgb(0x88, 0x32, 0xD7, 0x4B)
                : Microsoft.UI.ColorHelper.FromArgb(0x88, 0xD6, 0x8B, 0x00));
        }

        if (VBCableStatusTextBlock is not null)
        {
            VBCableStatusTextBlock.Text = hasCable
                ? "VB-CABLE is detected. Everything is working normally."
                : "VB-CABLE is not installed.";
        }

        if (VBCableBridgeInfoTextBlock is not null)
        {
            VBCableBridgeInfoTextBlock.Text = hasCable
                ? "Use the VB-CABLE virtual microphone in Discord, OBS, Telegram, games, and other apps. In many apps it is shown as CABLE Output / VB-Audio Virtual Cable. VoiSee sends audio to CABLE Input automatically."
                : "Install VB-CABLE to create the virtual microphone bridge. After installation, restart Windows if VoiSee still does not detect CABLE Input, then click Refresh Devices.";
        }

        if (InstallVBCableButton is not null)
        {
            InstallVBCableButton.IsEnabled = !hasCable;
            InstallVBCableButton.Visibility = hasCable ? Visibility.Collapsed : Visibility.Visible;
        }

        if (StartEngineButton is not null)
        {
            StartEngineButton.IsEnabled = hasCable;
        }

        if (!hasCable && _engine is null && EngineStatusTextBlock is not null)
        {
            EngineStatusTextBlock.Text = "VB-CABLE required";
        }
        else if (hasCable && _engine is null && EngineStatusTextBlock is not null)
        {
            EngineStatusTextBlock.Text = "Stopped";
        }
    }

    private void OnInstallVBCableClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var installerPath = PrepareBundledVBCableInstaller();
            if (installerPath is null)
            {
                AppendLog("Bundled VB-CABLE installer was not found. Opening VB-CABLE download page.");
                Process.Start(new ProcessStartInfo(VBCableDownloadUrl) { UseShellExecute = true });
                return;
            }

            AppendLog($"Starting VB-CABLE installer from full package folder: {installerPath}");
            Process.Start(new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory
            });

            AppendLog("After VB-CABLE installation, restart Windows or click Refresh Devices if the device appears without reboot.");
        }
        catch (Exception ex)
        {
            AppendLog($"VB-CABLE installer start error: {ex.Message}");
        }
    }

    private static string? PrepareBundledVBCableInstaller()
    {
        var bundleDir = Path.Combine(AppContext.BaseDirectory, "ThirdParty", "VB-CABLE");
        if (!Directory.Exists(bundleDir))
        {
            return null;
        }

        // Prefer an already extracted full package. Running a copied setup EXE alone fails,
        // because VB-CABLE setup needs INF/CAT/SYS files next to it.
        var extractedDir = Path.Combine(bundleDir, "_extracted");
        var extractedSetup = FindVBCableSetupExecutable(extractedDir, requireDriverPackage: true);
        if (extractedSetup is not null)
        {
            return extractedSetup;
        }

        // If the original ZIP is bundled, extract it to a temp folder and run setup from that
        // complete unzipped package.
        var zip = Directory.EnumerateFiles(bundleDir, "*.zip", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (zip is not null)
        {
            var extractDir = Path.Combine(
                Path.GetTempPath(),
                "VoiSee",
                "VB-CABLE",
                Path.GetFileNameWithoutExtension(zip));

            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zip, extractDir);
            return FindVBCableSetupExecutable(extractDir, requireDriverPackage: true);
        }

        // Last fallback: support a manually placed fully extracted VB-CABLE package.
        return FindVBCableSetupExecutable(bundleDir, requireDriverPackage: true);
    }

    private static string? FindVBCableSetupExecutable(string root, bool requireDriverPackage = false)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        var preferredNames = new[]
        {
            "VBCABLE_Setup_x64.exe",
            "VBCABLE_Setup.exe"
        };

        foreach (var name in preferredNames)
        {
            var path = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories)
                .FirstOrDefault(candidate => !requireDriverPackage || HasVBCableDriverPackageNearSetup(candidate));
            if (path is not null)
            {
                return path;
            }
        }

        return Directory.EnumerateFiles(root, "*Setup*x64*.exe", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*Setup*.exe", SearchOption.AllDirectories))
            .FirstOrDefault(candidate => !requireDriverPackage || HasVBCableDriverPackageNearSetup(candidate));
    }

    private static bool HasVBCableDriverPackageNearSetup(string setupPath)
    {
        var setupDir = Path.GetDirectoryName(setupPath);
        if (string.IsNullOrWhiteSpace(setupDir) || !Directory.Exists(setupDir))
        {
            return false;
        }

        // Official VB-CABLE setup expects the INF/CAT/SYS package to be available from
        // the same unzipped folder. Checking recursively is intentionally permissive for
        // future package layouts.
        return Directory.EnumerateFiles(setupDir, "*.inf", SearchOption.AllDirectories).Any()
            && Directory.EnumerateFiles(setupDir, "*.cat", SearchOption.AllDirectories).Any()
            && Directory.EnumerateFiles(setupDir, "*.sys", SearchOption.AllDirectories).Any();
    }

    private void WarmSoundCacheInBackground(IEnumerable<string>? paths = null)
    {
        var engine = _engine;
        if (engine is null)
        {
            return;
        }

        var soundPaths = (paths ?? _library.Sounds.Select(sound => sound.FilePath))
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (soundPaths.Count == 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            foreach (var path in soundPaths)
            {
                try
                {
                    engine.PreloadSoundAsync(path).GetAwaiter().GetResult();
                }
                catch
                {
                    // Best-effort warmup only. Playback reports real errors.
                }
            }
        });
    }

    private void ApplyStoredScalarSettingsToControls()
    {
        _soundFilePath = string.IsNullOrWhiteSpace(_settings.LastSoundFilePath) ? null : _settings.LastSoundFilePath;

        VirtualOutputVolumeSlider.Value = Clamp(_settings.VirtualMicMasterVolume, 0, 1.5);
        SoundVirtualVolumeSlider.Value = Clamp(_settings.SoundBoardVirtualMicVolume, 0, 1.5);
        SoundMonitorVolumeSlider.Value = Clamp(_settings.SoundBoardHeadphonesVolume, 0, 1.5);
        SoundVirtualDelaySlider.Value = Clamp(_settings.SoundBoardVirtualMicDelayMs, 0, 300);
        SetVoiceControl(VoiceGainSlider, VoiceGainValueBox, _settings.VoiceGain);
        SetVoiceControl(GateThresholdSlider, GateThresholdValueBox, _settings.VoiceGate);
        SetVoiceControl(CompressorThresholdSlider, CompressorThresholdValueBox, _settings.VoiceCompressor);
        SetVoiceControl(PitchSlider, PitchValueBox, _settings.VoicePitch);
        SetVoiceControl(FormantSlider, FormantValueBox, _settings.VoiceFormant);
        SetVoiceControl(BassSlider, BassValueBox, _settings.VoiceBass);
        SetVoiceControl(TrebleSlider, TrebleValueBox, _settings.VoiceTreble);
        SetVoiceControl(DistortionSlider, DistortionValueBox, _settings.VoiceDistortion);
        SetVoiceControl(RobotSlider, RobotValueBox, _settings.VoiceRobot);
        SetVoiceControl(TremoloSlider, TremoloValueBox, _settings.VoiceTremolo);
        SetVoiceControl(EchoSlider, EchoValueBox, _settings.VoiceEcho);
        SetVoiceControl(ReverbSlider, ReverbValueBox, _settings.VoiceReverb);
        SetVoiceControl(RadioSlider, RadioValueBox, _settings.VoiceRadio);
        SetVoiceControl(BitCrusherSlider, BitCrusherValueBox, _settings.VoiceBitCrusher);
        SetVoiceControl(AlienSlider, AlienValueBox, _settings.VoiceAlien);
        _voiceMonitorEnabled = _settings.VoiceMonitorEnabled;
        UpdateAllLabels();
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value))
        {
            return min;
        }

        return Math.Min(max, Math.Max(min, value));
    }

    private void OnMainTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSoundInputOverlayBounds();
    }

    private void InstallSoundBoardWheelHook()
    {
        try
        {
            _lowLevelMouseProc = LowLevelMouseHookProc;
            _mouseHookHandle = SetWindowsHookEx(WhMouseLl, _lowLevelMouseProc, GetModuleHandle(null), 0);
            if (_mouseHookHandle == IntPtr.Zero)
            {
                AppendLog("Mouse wheel hook was not installed; local SoundBoard wheel handling remains active.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Mouse wheel hook error: {ex.Message}");
        }
    }

    private void InstallGlobalKeyboardHook()
    {
        try
        {
            _lowLevelKeyboardProc = LowLevelKeyboardHookProc;
            _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _lowLevelKeyboardProc, GetModuleHandle(null), 0);
            if (_keyboardHookHandle == IntPtr.Zero)
            {
                AppendLog("Global hotkey hook was not installed. Hotkeys will work only through UI buttons.");
            }
            else
            {
                AppendLog("Global hotkey hook installed.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Global hotkey hook error: {ex.Message}");
        }
    }

    private IntPtr LowLevelKeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            if (_capturingHotkey)
            {
                return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
            }

            if (message == WmKeyDown || message == WmSysKeyDown || message == WmKeyUp || message == WmSysKeyUp)
            {
                try
                {
                    var data = Marshal.PtrToStructure<KeyboardLowLevelHookStruct>(lParam);
                    var isKeyDown = message == WmKeyDown || message == WmSysKeyDown;
                    var isKeyUp = message == WmKeyUp || message == WmSysKeyUp;
                    if (TryHandleGlobalHotkey(data.VkCode, isKeyDown, isKeyUp))
                    {
                        return new IntPtr(1);
                    }
                }
                catch
                {
                    // Never let the hotkey hook break normal keyboard input.
                }
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private IntPtr LowLevelMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WmMouseWheel)
        {
            try
            {
                if (TryHandleMainTabWheel(lParam))
                {
                    return new IntPtr(1);
                }
            }
            catch
            {
                // Never let the diagnostic wheel hook break normal mouse input.
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private bool TryHandleMainTabWheel(IntPtr lParam)
    {
        if (MainTabView is null || _windowHandleIsUnavailable())
        {
            return false;
        }

        var foreground = GetForegroundWindow();
        if (foreground != _windowHandle)
        {
            return false;
        }

        var hookData = Marshal.PtrToStructure<MouseLowLevelHookStruct>(lParam);
        var clientPoint = hookData.Point;
        if (!ScreenToClient(_windowHandle, ref clientPoint))
        {
            return false;
        }

        if (RootGrid is null)
        {
            return false;
        }

        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        var xDip = clientPoint.X / scale;
        var yDip = clientPoint.Y / scale;
        var delta = unchecked((short)((hookData.MouseData >> 16) & 0xffff));
        if (delta == 0)
        {
            return false;
        }

        // Gate 9.2.7: the modal Sound Editor owns the mouse wheel while it is open.
        // Route the low-level hook directly to its ScrollViewer before any main-tab
        // routing, otherwise the large Gate 6.8 SoundBoard wheel zone steals it.
        if (_soundEditorActive)
        {
            return _activeSoundEditorScrollViewer is not null
                && TryScrollViewer(_activeSoundEditorScrollViewer, delta, 58.0);
        }

        if (_suppressMainTabWheelRouting)
        {
            return TryHandleActiveIconPickerWheel(xDip, yDip, delta);
        }

        return MainTabView.SelectedIndex switch
        {
            0 => IsPointInSoundBoardWheelZone(xDip, yDip) && TryScrollSoundOverlay(delta),
            1 => IsPointInVoiceChangerWheelZone(yDip) && TryScrollVoiceChanger(delta),
            2 => TryHandleScenesWheel(xDip, yDip, delta),
            3 => TryHandleSettingsWheel(xDip, yDip, delta),
            _ => false
        };
    }

    private bool IsPointInSoundBoardWheelZone(double xDip, double yDip)
    {
        if (RootGrid is null || SoundBoardTabRoot is null)
        {
            return false;
        }

        // Gate 6.8: keep the known-good Gate 6.5 / Gate 5.34 SoundBoard wheel calibration.
        // Gate 6.6 changed this to a centered client-pixel zone and broke the SoundBoard scroll area.
        var tabTop = SoundBoardTabRoot.TransformToVisual(RootGrid)
            .TransformPoint(new Windows.Foundation.Point(0, 0))
            .Y;
        var usableHeight = Math.Max(1.0, RootGrid.ActualHeight - tabTop);

        var zoneLeft = 0.0;
        var zoneTop = Math.Max(tabTop, tabTop - usableHeight * SoundWheelZoneExpandUpRatio);
        var zoneRight = RootGrid.ActualWidth * (1.0 + SoundWheelZoneExpandRightRatio);
        var zoneBottom = RootGrid.ActualHeight + usableHeight * SoundWheelZoneExpandBottomRatio;

        return xDip >= zoneLeft && xDip <= zoneRight && yDip >= zoneTop && yDip <= zoneBottom;
    }

    private bool IsPointInVoiceChangerWheelZone(double yDip)
    {
        // Gate 6.11: SoundBoard worked because its wheel zone is allowed to extend
        // below RootGrid.ActualHeight. Voice Changer used to be clipped at
        // RootGrid.ActualHeight, which created a dead lower area in fullscreen.
        return IsPointInExtendedVerticalWheelZone(VoiceChangerScrollViewer, yDip);
    }

    private bool IsPointInExtendedVerticalWheelZone(FrameworkElement? element, double yDip)
    {
        if (RootGrid is null || element is null)
        {
            return false;
        }

        var top = GetElementTopDip(element);
        var usableHeight = Math.Max(1.0, RootGrid.ActualHeight - top);
        var bottom = RootGrid.ActualHeight + usableHeight * SoundWheelZoneExpandBottomRatio;
        return yDip >= top && yDip <= bottom;
    }

    private bool IsPointInElementWheelZone(FrameworkElement? element, double xDip, double yDip, bool extendBottom, double bottomExtensionRatio = 0.0, double leftExtensionRatio = 0.0, double rightExtensionRatio = 0.0, double horizontalShiftRatio = 0.0)
    {
        if (RootGrid is null || element is null)
        {
            return false;
        }

        try
        {
            var topLeft = element.TransformToVisual(RootGrid)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            var width = Math.Max(1.0, element.ActualWidth);
            var height = Math.Max(1.0, element.ActualHeight);
            var horizontalShift = width * horizontalShiftRatio;
            var left = topLeft.X + horizontalShift;
            var top = topLeft.Y;
            var right = left + width;
            var bottom = top + height;

            if (leftExtensionRatio > 0.0)
            {
                left -= width * leftExtensionRatio;
            }

            if (rightExtensionRatio > 0.0)
            {
                right += width * rightExtensionRatio;
            }

            if (bottomExtensionRatio > 0.0)
            {
                bottom += height * bottomExtensionRatio;
            }

            if (extendBottom)
            {
                var usableHeight = Math.Max(1.0, RootGrid.ActualHeight - top);
                bottom = Math.Max(bottom, RootGrid.ActualHeight + usableHeight * SoundWheelZoneExpandBottomRatio);
            }

            return xDip >= left && xDip <= right && yDip >= top && yDip <= bottom;
        }
        catch
        {
            return false;
        }
    }

    private bool TryHandleActiveIconPickerWheel(double xDip, double yDip, int wheelDelta)
    {
        var scrollViewer = _activeIconPickerScrollViewer;
        var wheelZone = _activeIconPickerWheelZoneElement;
        if (scrollViewer is null || wheelZone is null)
        {
            return false;
        }

        // Gate 8.1 buildfix 2: modal wheel zones are configurable per popup.
        // Logs keep the left/down expansion from buildfix 1. The icon picker
        // uses right/down expansion so the wheel works in the area where the
        // picker visually opens without stealing the left side unnecessarily.
        return IsPointInElementWheelZone(
                wheelZone,
                xDip,
                yDip,
                extendBottom: false,
                bottomExtensionRatio: ModalWheelZoneExpandBottomRatio,
                leftExtensionRatio: _activeModalWheelZoneLeftExtensionRatio,
                rightExtensionRatio: _activeModalWheelZoneRightExtensionRatio,
                horizontalShiftRatio: _activeModalWheelZoneHorizontalShiftRatio)
            && TryScrollViewer(scrollViewer, wheelDelta, 52.0);
    }

    private bool IsPointInCompactElementWheelZone(FrameworkElement? element, double yDip, double heightMultiplier)
    {
        if (RootGrid is null || element is null)
        {
            return false;
        }

        var top = GetElementTopDip(element);
        var elementHeight = Math.Max(1.0, element.ActualHeight);
        var bottom = top + elementHeight * Math.Max(1.0, heightMultiplier);
        return yDip >= top && yDip <= bottom;
    }

    private double GetElementTopDip(FrameworkElement? element)
    {
        if (RootGrid is null || element is null)
        {
            return 0.0;
        }

        try
        {
            return element.TransformToVisual(RootGrid)
                .TransformPoint(new Windows.Foundation.Point(0, 0))
                .Y;
        }
        catch
        {
            return 0.0;
        }
    }

    private bool TryScrollVoiceChanger(int wheelDelta)
    {
        return TryScrollViewer(VoiceChangerScrollViewer, wheelDelta, 42.0);
    }

    private bool TryHandleScenesWheel(double xDip, double yDip, int wheelDelta)
    {
        // Gate 7.10 buildfix 3: the scene list and scene sound buttons must own
        // separate horizontal zones, but the left scene list lower wheel zone
        // is extended by 65% so scrolling still works near the bottom controls.
        if (IsPointInElementWheelZone(ScenesListView, xDip, yDip, extendBottom: false, bottomExtensionRatio: SceneListWheelZoneExpandDownRatio, rightExtensionRatio: SceneListWheelZoneExpandRightRatio))
        {
            var sceneListScrollViewer = FindDescendantScrollViewer(ScenesListView);
            return sceneListScrollViewer is not null
                ? TryScrollViewer(sceneListScrollViewer, wheelDelta, 42.0)
                : false;
        }

        if (IsPointInElementWheelZone(SceneSoundButtonsScrollViewer, xDip, yDip, extendBottom: true, rightExtensionRatio: SceneSoundButtonsWheelZoneExpandRightRatio))
        {
            return TryScrollViewer(SceneSoundButtonsScrollViewer, wheelDelta, 42.0);
        }

        return false;
    }

    private bool TryHandleSettingsWheel(double xDip, double yDip, int wheelDelta)
    {
        if (IsPointInElementWheelZone(SettingsScrollViewer, xDip, yDip, extendBottom: true, rightExtensionRatio: SettingsWheelZoneExpandRightRatio))
        {
            return TryScrollViewer(SettingsScrollViewer, wheelDelta, 42.0);
        }

        return false;
    }

    private static bool TryScrollViewer(ScrollViewer? scrollViewer, int wheelDelta, double pixelsPerNotch)
    {
        if (scrollViewer is null || wheelDelta == 0)
        {
            return false;
        }

        var notches = Math.Max(1.0, Math.Abs(wheelDelta) / 120.0);
        var step = pixelsPerNotch * notches;
        var target = scrollViewer.VerticalOffset - Math.Sign(wheelDelta) * step;
        target = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, target));
        scrollViewer.ChangeView(null, target, null, disableAnimation: false);
        return true;
    }

    private async void OnOpenLogFullscreenClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var logText = _logBuffer.ToString();
            if (string.IsNullOrWhiteSpace(logText))
            {
                logText = "No log entries yet.";
            }

            var textBlock = new TextBlock
            {
                Text = logText,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                IsTextSelectionEnabled = true
            };

            var scrollViewer = new ScrollViewer
            {
                Content = textBlock,
                Width = Math.Max(900, RootGrid?.ActualWidth * 0.82 ?? 900),
                Height = Math.Max(420, Math.Min(560, RootGrid?.ActualHeight * 0.62 ?? 520)),
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                VerticalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                ZoomMode = ZoomMode.Disabled
            };
            scrollViewer.Loaded += (_, _) => DispatcherQueue.TryEnqueue(() => scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null, disableAnimation: true));
            AttachIconPickerWheelRouting(scrollViewer, scrollViewer);
            AttachIconPickerWheelRouting(textBlock, scrollViewer);

            var dialog = new ContentDialog
            {
                Title = "Application log",
                Content = scrollViewer,
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };

            _suppressMainTabWheelRouting = true;
            _activeIconPickerScrollViewer = scrollViewer;
            _activeIconPickerWheelZoneElement = scrollViewer;
            _activeModalWheelZoneLeftExtensionRatio = ModalWheelZoneExpandLeftRatio;
            _activeModalWheelZoneRightExtensionRatio = 0.0;
            _activeModalWheelZoneHorizontalShiftRatio = 0.0;
            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                _activeIconPickerScrollViewer = null;
                _activeIconPickerWheelZoneElement = null;
                _activeModalWheelZoneLeftExtensionRatio = 0.0;
                _activeModalWheelZoneRightExtensionRatio = 0.0;
                _activeModalWheelZoneHorizontalShiftRatio = 0.0;
                _suppressMainTabWheelRouting = false;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Open logs error: {ex.Message}");
        }
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var nested = FindDescendantScrollViewer(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private bool _windowHandleIsUnavailable() => _windowHandle == IntPtr.Zero;

    private void UpdateSoundInputOverlayBounds()
    {
        // Gate 5.34: SoundInputOverlay is still placed directly inside SoundListArea.
        // It stretches with the Sounds list, so no window-level coordinate transform is used.
        if (SoundInputOverlay is null || MainTabView is null)
        {
            return;
        }

        SoundInputOverlay.Visibility = MainTabView.SelectedIndex == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnSoundInputOverlayPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(SoundInputOverlay).Properties.MouseWheelDelta;
        if (delta != 0 && TryScrollSoundOverlay(delta))
        {
            e.Handled = true;
        }
    }

    private bool TryScrollSoundOverlay(int wheelDelta)
    {
        // Smaller step than the native 120px-style jumps: about one row per wheel notch.
        return TryScrollViewer(SoundOverlayScrollViewer, wheelDelta, SoundBoardWheelPixelsPerNotch);
    }

    private bool TryHandleGlobalHotkey(int vkCode, bool isKeyDown, bool isKeyUp)
    {
        if (IsModifierKey(vkCode))
        {
            return false;
        }

        if (isKeyUp)
        {
            return TryHandlePushToTalkRelease(vkCode);
        }

        if (!isKeyDown)
        {
            return false;
        }

        var currentMaybe = HotkeyGesture.FromKeyboardState(vkCode, IsModifierDown);
        if (!currentMaybe.HasValue)
        {
            return false;
        }

        var current = currentMaybe.Value;

        // Gate 9.2.6: while the modal Sound Editor is open, all normal VoiSee
        // hotkeys are disabled. Only the configured Play/Pause and Stop keys
        // are redirected to the editor preview state machine.
        if (_soundEditorActive)
        {
            if ((HotkeyGesture.TryParse(_settings.SoundBoardPlayHotkey, out var editorPlayPause) && editorPlayPause.Equals(current)) ||
                (HotkeyGesture.TryParse(_settings.SoundBoardPauseHotkey, out var editorLegacyPause) && editorLegacyPause.Equals(current)))
            {
                DispatcherQueue.TryEnqueue(() => _soundEditorPlayPauseAction?.Invoke());
                return true;
            }

            if (HotkeyGesture.TryParse(_settings.SoundBoardStopHotkey, out var editorStop) && editorStop.Equals(current))
            {
                DispatcherQueue.TryEnqueue(() => _soundEditorStopAction?.Invoke());
                return true;
            }

            return false;
        }

        // Only plain English letter keys and < > { } are local-only.
        // This keeps common typing keys like H from being stolen in Telegram/Discord/browser text fields.
        // Other single keys and Ctrl/Alt/Shift combinations remain global when explicitly assigned.
        if (IsLocalOnlyPlainHotkey(current))
        {
            if (GetForegroundWindow() != _windowHandle || IsTextInputFocused())
            {
                return false;
            }
        }

        if (TryHandleVirtualMicMuteHotkey(current)) return true;
        if (TryHandleTransportHotkey(current)) return true;
        if (TryHandleSceneHotkey(current)) return true;
        if (TryHandleSoundHotkey(current)) return true;
        if (TryHandleVoicePresetHotkey(current)) return true;

        return false;
    }

    private VoiSeScene? GetActiveScene()
    {
        return string.IsNullOrWhiteSpace(_activeSceneId)
            ? null
            : _scenes.FirstOrDefault(scene => string.Equals(scene.Id, _activeSceneId, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryHandleVirtualMicMuteHotkey(HotkeyGesture current)
    {
        if (!HotkeyGesture.TryParse(_settings.VirtualMicMuteHotkey, out var muteHotkey) || !muteHotkey.Equals(current))
        {
            return false;
        }

        DispatcherQueue.TryEnqueue(() => ToggleVirtualMicMute("Hotkey"));
        return true;
    }

    private bool TryHandleSceneHotkey(HotkeyGesture current)
    {
        var activeScene = GetActiveScene();
        if (activeScene is null)
        {
            return false;
        }

        foreach (var sceneButton in activeScene.SoundButtons.OrderBy(button => button.IsLooped ? 0 : 1).ThenBy(button => button.SortOrder))
        {
            if (!HotkeyGesture.TryParse(sceneButton.SceneHotkey, out var configured) || !configured.Equals(current))
            {
                continue;
            }

            var sound = PickSound(sceneButton.SoundId);
            if (sound is null)
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(sceneButton.LocalName) ? sound.DisplayName : sceneButton.LocalName!.Trim();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (sceneButton.IsLooped)
                {
                    PlaySceneSound(activeScene, sceneButton, sound, true, "Scene looped sound");
                }
                else
                {
                    // Scene sound hotkeys intentionally restart the assigned one-shot from the beginning.
                    // Mouse clicks keep the play/pause/resume behavior.
                    PlaySceneSound(activeScene, sceneButton, sound, false, "Scene sound hotkey");
                }

                AppendLog($"Scene hotkey: {activeScene.Name} / {displayName} [{configured}]");
            });
            return true;
        }

        return false;
    }

    private bool TryHandleSoundHotkey(HotkeyGesture current)
    {
        if (IsSceneActive)
        {
            foreach (var sound in _library.Sounds)
            {
                if (HotkeyGesture.TryParse(sound.Hotkey, out var blocked) && blocked.Equals(current))
                {
                    DispatcherQueue.TryEnqueue(() => AppendLog("SoundBoard hotkey blocked while a scene is active."));
                    return true;
                }
            }

            return false;
        }

        foreach (var sound in _library.Sounds)
        {
            if (HotkeyGesture.TryParse(sound.Hotkey, out var configured) && configured.Equals(current))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    SelectSound(sound);
                    PlaySelectedSound();
                    AppendLog($"Sound hotkey: {sound.DisplayName} [{configured}]");
                });
                return true;
            }
        }

        return false;
    }

    private bool TryHandleVoicePresetHotkey(HotkeyGesture current)
    {
        foreach (var preset in _voicePresets)
        {
            if (HotkeyGesture.TryParse(preset.PresetHotkey, out var presetHotkey) && presetHotkey.Equals(current))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ApplyVoicePreset(preset);
                    AppendLog($"Voice preset hotkey: {preset.Name} [{presetHotkey}]");
                });
                return true;
            }

            if (HotkeyGesture.TryParse(preset.PushToTalkHotkey, out var pushHotkey) && pushHotkey.Equals(current))
            {
                if (_activePushToTalkGesture is not null)
                {
                    return true;
                }

                _activePushToTalkGesture = pushHotkey;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _pushToTalkPreviousVoicePreset = CaptureCurrentVoicePreset("Before push to talk");
                    ApplyVoicePreset(preset);
                    AppendLog($"Push-to-talk voice preset on: {preset.Name} [{pushHotkey}]");
                });
                return true;
            }
        }

        return false;
    }

    private bool TryHandlePushToTalkRelease(int vkCode)
    {
        if (!_activePushToTalkGesture.HasValue || _activePushToTalkGesture.Value.KeyCode != vkCode)
        {
            return false;
        }

        var gesture = _activePushToTalkGesture.Value;
        _activePushToTalkGesture = null;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_pushToTalkPreviousVoicePreset is not null)
            {
                ApplyVoicePreset(_pushToTalkPreviousVoicePreset);
                _pushToTalkPreviousVoicePreset = null;
                AppendLog($"Push-to-talk voice preset off: [{gesture}]");
            }
        });
        return true;
    }

    private bool TryHandleTransportHotkey(HotkeyGesture current)
    {
        var activeScene = GetActiveScene();
        var sceneActive = activeScene is not null;

        if (sceneActive && HotkeyGesture.TryParse(_settings.DisableSceneHotkey, out var disableScene) && disableScene.Equals(current))
        {
            DispatcherQueue.TryEnqueue(() => DisableActiveScene($"Hotkey: scene disabled [{disableScene}]."));
            return true;
        }

        if (HotkeyGesture.TryParse(_settings.SoundBoardPlayHotkey, out var playPause) && playPause.Equals(current))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (activeScene is not null)
                {
                    ToggleSceneOneShotSoundsPause(activeScene);
                }
                else
                {
                    TransportPlayPause();
                }
            });
            return true;
        }

        // Legacy fallback: old settings files may still contain a separate Pause hotkey.
        if (HotkeyGesture.TryParse(_settings.SoundBoardPauseHotkey, out var legacyPause) && legacyPause.Equals(current))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (activeScene is not null)
                {
                    ToggleSceneOneShotSoundsPause(activeScene);
                }
                else
                {
                    TransportPlayPause();
                }
            });
            return true;
        }

        if (HotkeyGesture.TryParse(_settings.SoundBoardStopHotkey, out var stop) && stop.Equals(current))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (activeScene is not null)
                {
                    StopSceneOneShotSounds(activeScene, "Transport hotkey: scene one-shot sounds stopped.");
                }
                else
                {
                    TransportStop();
                }
            });
            return true;
        }

        if (HotkeyGesture.TryParse(_settings.SoundBoardNextHotkey, out var next) && next.Equals(current))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (sceneActive)
                {
                    AppendLog("Next hotkey blocked while a scene is active.");
                }
                else
                {
                    SelectRelativeSound(1, play: true);
                }
            });
            return true;
        }

        if (HotkeyGesture.TryParse(_settings.SoundBoardPreviousHotkey, out var previous) && previous.Equals(current))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (sceneActive)
                {
                    AppendLog("Previous hotkey blocked while a scene is active.");
                }
                else
                {
                    SelectRelativeSound(-1, play: true);
                }
            });
            return true;
        }

        return false;
    }

    private void TransportPlayPause()
    {
        var status = _engine?.GetSoundStatus() ?? SoundboardStatus.Empty;
        if (status.IsActive)
        {
            _engine?.ToggleSoundPause();
            UpdateTimeline();
            AppendLog(status.IsPaused ? "Transport hotkey: resume." : "Transport hotkey: pause.");
            return;
        }

        PlaySelectedSound();
        AppendLog("Transport hotkey: play.");
    }

    private void TransportPlay()
    {
        var status = _engine?.GetSoundStatus() ?? SoundboardStatus.Empty;
        if (status.IsActive && status.IsPaused)
        {
            _engine?.ToggleSoundPause();
            UpdateTimeline();
            AppendLog("Transport hotkey: play/resume.");
            return;
        }

        if (!status.IsActive)
        {
            PlaySelectedSound();
            AppendLog("Transport hotkey: play.");
        }
    }

    private void TransportPause()
    {
        var status = _engine?.GetSoundStatus() ?? SoundboardStatus.Empty;
        if (status.IsActive && !status.IsPaused)
        {
            _engine?.ToggleSoundPause();
            UpdateTimeline();
            AppendLog("Transport hotkey: pause.");
        }
    }

    private void TransportStop()
    {
        _engine?.StopSound();
        _currentSoundDisplayName = null;
        UpdateTimeline();
        AppendLog("Transport hotkey: stop.");
    }

    private static bool IsModifierKey(int vkCode) => vkCode is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static bool IsModifierDown(int vkCode) => (GetAsyncKeyState(vkCode) & unchecked((short)0x8000)) != 0;

    private bool IsTextInputFocused()
    {
        try
        {
            if (Content is not FrameworkElement root)
            {
                return false;
            }

            var focused = FocusManager.GetFocusedElement(root.XamlRoot);
            return focused is TextBox or PasswordBox;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLocalOnlyPlainHotkey(HotkeyGesture gesture)
    {
        return !gesture.HasModifier && IsLocalOnlyPlainKeyCode(gesture.KeyCode);
    }

    private static bool IsLocalOnlyPlainKeyCode(int keyCode)
    {
        return keyCode is >= 0x41 and <= 0x5A // A-Z
            or 0xBC // < / comma key
            or 0xBE // > / period key
            or 0xDB // { / [ key
            or 0xDD; // } / ] key
    }

    private async void OnConfigureTransportHotkeysClick(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "Click a hotkey button, then press a key or Ctrl/Alt/Shift combination. Esc cancels capture. Plain A-Z and < > { } are local-only; NumPad keys and Ctrl/Alt/Shift combinations remain global.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });

        panel.Children.Add(CreateHotkeyCaptureRow("Play / Pause", _settings.SoundBoardPlayHotkey, out var playPauseButton));
        panel.Children.Add(CreateHotkeyCaptureRow("Stop", _settings.SoundBoardStopHotkey, out var stopButton));
        panel.Children.Add(CreateHotkeyCaptureRow("Next", _settings.SoundBoardNextHotkey, out var nextButton));
        panel.Children.Add(CreateHotkeyCaptureRow("Previous", _settings.SoundBoardPreviousHotkey, out var previousButton));
        panel.Children.Add(CreateHotkeyCaptureRow("Disable scene", _settings.DisableSceneHotkey, out var disableSceneButton));
        panel.Children.Add(CreateHotkeyCaptureRow("Virtual Mic Mute", _settings.VirtualMicMuteHotkey, out var virtualMicMuteButton));

        var dialog = new ContentDialog
        {
            Title = "Hotkeys",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        _capturingHotkey = true;
        try
        {
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }
        }
        finally
        {
            _capturingHotkey = false;
        }

        _settings.SoundBoardPlayHotkey = NormalizeOptionalHotkey(GetHotkeyButtonValue(playPauseButton));
        _settings.SoundBoardPauseHotkey = null;
        _settings.SoundBoardStopHotkey = NormalizeOptionalHotkey(GetHotkeyButtonValue(stopButton));
        _settings.SoundBoardNextHotkey = NormalizeOptionalHotkey(GetHotkeyButtonValue(nextButton));
        _settings.SoundBoardPreviousHotkey = NormalizeOptionalHotkey(GetHotkeyButtonValue(previousButton));
        _settings.DisableSceneHotkey = NormalizeOptionalHotkey(GetHotkeyButtonValue(disableSceneButton));
        _settings.VirtualMicMuteHotkey = NormalizeOptionalHotkey(GetHotkeyButtonValue(virtualMicMuteButton));
        _settingsStore.Save(_settings);
        UpdateTransportHotkeySummary();
        AppendLog("Hotkeys updated.");
    }

    private FrameworkElement CreateHotkeyCaptureRow(string header, string? initialValue, out Button captureButton)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 4
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = header,
            FontSize = 12,
            Opacity = 0.72
        };
        Grid.SetColumn(label, 0);
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        captureButton = CreateHotkeyCaptureButton(initialValue);
        var capturedButton = captureButton;
        Grid.SetColumn(capturedButton, 0);
        Grid.SetRow(capturedButton, 1);
        grid.Children.Add(capturedButton);

        var clearButton = new Button
        {
            Content = "✕",
            MinWidth = 42,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        clearButton.Click += (_, _) =>
        {
            capturedButton.Tag = null;
            SetHotkeyButtonText(capturedButton, null);
        };
        Grid.SetColumn(clearButton, 1);
        Grid.SetRow(clearButton, 1);
        grid.Children.Add(clearButton);

        return grid;
    }

    private Button CreateHotkeyCaptureButton(string? initialValue)
    {
        var button = new Button
        {
            Content = string.Empty,
            MinWidth = 380,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 8, 12, 8)
        };

        SetHotkeyButtonText(button, initialValue);
        button.Click += OnHotkeyCaptureButtonClick;
        button.KeyDown += OnHotkeyCaptureButtonKeyDown;
        button.LostFocus += OnHotkeyCaptureButtonLostFocus;
        return button;
    }

    private sealed class HotkeyCaptureState
    {
        public HotkeyCaptureState(string? previousText)
        {
            PreviousText = previousText;
        }

        public string? PreviousText { get; }
    }

    private static void SetHotkeyButtonText(Button button, string? hotkey)
    {
        button.Content = string.IsNullOrWhiteSpace(hotkey) ? "—" : hotkey.Trim();
    }

    private static string? GetHotkeyButtonValue(Button button)
    {
        var text = button.Content?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "—" || text.StartsWith("Press", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return text;
    }

    private void OnHotkeyCaptureButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var previous = GetHotkeyButtonValue(button);
        button.Tag = new HotkeyCaptureState(previous);
        button.Content = "Press key…  Esc cancels";
        button.Focus(FocusState.Programmatic);
    }

    private void OnHotkeyCaptureButtonLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HotkeyCaptureState state } button)
        {
            SetHotkeyButtonText(button, state.PreviousText);
            button.Tag = null;
        }
    }

    private void OnHotkeyCaptureButtonKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not HotkeyCaptureState state)
        {
            return;
        }

        var vkCode = (int)e.Key;
        if (vkCode == 0x1B) // Esc cancels capture.
        {
            SetHotkeyButtonText(button, state.PreviousText);
            button.Tag = null;
            e.Handled = true;
            return;
        }

        if (IsModifierKey(vkCode))
        {
            e.Handled = true;
            return;
        }

        var gestureMaybe = HotkeyGesture.FromKeyboardState(vkCode, IsModifierDown);
        if (gestureMaybe.HasValue)
        {
            SetHotkeyButtonText(button, gestureMaybe.Value.ToString());
            button.Tag = null;
            e.Handled = true;
        }
    }

    private async Task<string?> CaptureHotkeyDialogAsync(string title, string description, string? initialValue)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });

        panel.Children.Add(CreateHotkeyCaptureRow("Hotkey", initialValue, out var hotkeyButton));

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        _capturingHotkey = true;
        try
        {
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return GetHotkeyButtonValue(hotkeyButton) ?? string.Empty;
        }
        finally
        {
            _capturingHotkey = false;
        }
    }

    private static string? NormalizeOptionalHotkey(string? text)
    {
        return HotkeyGesture.TryParse(text, out var gesture) ? gesture.ToString() : null;
    }

    private void UpdateTransportHotkeySummary()
    {
        if (TransportHotkeysSummaryTextBlock is null)
        {
            return;
        }

        var parts = new[]
        {
            $"Play/Pause: {(_settings.SoundBoardPlayHotkey ?? "—")}",
            $"Stop: {(_settings.SoundBoardStopHotkey ?? "—")}",
            $"Next: {(_settings.SoundBoardNextHotkey ?? "—")}",
            $"Prev: {(_settings.SoundBoardPreviousHotkey ?? "—")}",
            $"Disable scene: {(_settings.DisableSceneHotkey ?? "—")}",
            $"Virtual Mic Mute: {(_settings.VirtualMicMuteHotkey ?? "—")}"
        };
        TransportHotkeysSummaryTextBlock.Text = "Hotkeys: " + string.Join("    ", parts);
    }

    private readonly record struct HotkeyGesture(bool Ctrl, bool Alt, bool Shift, int KeyCode)
    {
        public bool HasModifier => Ctrl || Alt || Shift;

        public static HotkeyGesture? FromKeyboardState(int vkCode, Func<int, bool> isDown)
        {
            if (!TryGetKeyName(vkCode, out _))
            {
                return null;
            }

            return new HotkeyGesture(
                isDown(0x11) || isDown(0xA2) || isDown(0xA3),
                isDown(0x12) || isDown(0xA4) || isDown(0xA5),
                isDown(0x10) || isDown(0xA0) || isDown(0xA1),
                vkCode);
        }

        public static bool TryParse(string? text, out HotkeyGesture gesture)
        {
            gesture = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var ctrl = false;
            var alt = false;
            var shift = false;
            int? keyCode = null;

            foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var part = rawPart.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    ctrl = true;
                    continue;
                }

                if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    alt = true;
                    continue;
                }

                if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    shift = true;
                    continue;
                }

                if (TryParseKeyCode(part, out var parsedKeyCode))
                {
                    keyCode = parsedKeyCode;
                }
            }

            if (keyCode is null)
            {
                return false;
            }

            gesture = new HotkeyGesture(ctrl, alt, shift, keyCode.Value);
            return true;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            parts.Add(TryGetKeyName(KeyCode, out var keyName) ? keyName : KeyCode.ToString());
            return string.Join("+", parts);
        }

        private static bool TryParseKeyCode(string token, out int keyCode)
        {
            keyCode = 0;
            var upper = token.Trim().ToUpperInvariant();
            if (upper.Length == 1)
            {
                var ch = upper[0];
                if (ch is >= 'A' and <= 'Z' || ch is >= '0' and <= '9')
                {
                    keyCode = ch;
                    return true;
                }

                keyCode = ch switch
                {
                    '<' => 0xBC,
                    '>' => 0xBE,
                    '{' => 0xDB,
                    '}' => 0xDD,
                    _ => 0
                };
                if (keyCode != 0)
                {
                    return true;
                }
            }

            if ((upper.StartsWith("NUMPAD") && int.TryParse(upper[6..], out var numpadLong) && numpadLong is >= 0 and <= 9)
                || (upper.StartsWith("NUM") && int.TryParse(upper[3..], out numpadLong) && numpadLong is >= 0 and <= 9))
            {
                keyCode = 0x60 + numpadLong;
                return true;
            }

            if (upper.StartsWith("F") && int.TryParse(upper[1..], out var fn) && fn is >= 1 and <= 24)
            {
                keyCode = 0x70 + fn - 1;
                return true;
            }

            keyCode = upper switch
            {
                "NUMPADMULTIPLY" or "NUMMULTIPLY" or "NUM*" => 0x6A,
                "NUMPADADD" or "NUMADD" or "NUM+" => 0x6B,
                "NUMPADSUBTRACT" or "NUMSUBTRACT" or "NUM-" => 0x6D,
                "NUMPADDECIMAL" or "NUMDECIMAL" or "NUM." => 0x6E,
                "NUMPADDIVIDE" or "NUMDIVIDE" or "NUM/" => 0x6F,
                "SPACE" => 0x20,
                "ENTER" or "RETURN" => 0x0D,
                "ESC" or "ESCAPE" => 0x1B,
                "TAB" => 0x09,
                "LEFT" => 0x25,
                "UP" => 0x26,
                "RIGHT" => 0x27,
                "DOWN" => 0x28,
                "HOME" => 0x24,
                "END" => 0x23,
                "PAGEUP" or "PGUP" => 0x21,
                "PAGEDOWN" or "PGDN" => 0x22,
                "INSERT" or "INS" => 0x2D,
                "DELETE" or "DEL" => 0x2E,
                "BACKSPACE" => 0x08,
                _ => 0
            };
            return keyCode != 0;
        }

        private static bool TryGetKeyName(int keyCode, out string keyName)
        {
            keyName = string.Empty;
            if (keyCode is >= 0x41 and <= 0x5A || keyCode is >= 0x30 and <= 0x39)
            {
                keyName = ((char)keyCode).ToString();
                return true;
            }

            keyName = keyCode switch
            {
                0xBC => "<",
                0xBE => ">",
                0xDB => "{",
                0xDD => "}",
                _ => string.Empty
            };
            if (keyName.Length > 0)
            {
                return true;
            }

            if (keyCode is >= 0x60 and <= 0x69)
            {
                keyName = "Num" + (keyCode - 0x60);
                return true;
            }

            keyName = keyCode switch
            {
                0x6A => "Num*",
                0x6B => "Num+",
                0x6D => "Num-",
                0x6E => "Num.",
                0x6F => "Num/",
                _ => string.Empty
            };
            if (keyName.Length > 0)
            {
                return true;
            }

            if (keyCode is >= 0x70 and <= 0x87)
            {
                keyName = "F" + (keyCode - 0x70 + 1);
                return true;
            }

            keyName = keyCode switch
            {
                0x20 => "Space",
                0x0D => "Enter",
                0x1B => "Esc",
                0x09 => "Tab",
                0x25 => "Left",
                0x26 => "Up",
                0x27 => "Right",
                0x28 => "Down",
                0x24 => "Home",
                0x23 => "End",
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x2D => "Insert",
                0x2E => "Delete",
                0x08 => "Backspace",
                _ => string.Empty
            };
            return keyName.Length > 0;
        }
    }

    private void OnSoundInputOverlayPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(SoundInputOverlay);
        var sound = TryGetSoundAtOverlayPoint(point.Position);
        if (sound is null)
        {
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            SelectSound(sound);
            var flyout = CreateSoundContextFlyout();
            var options = new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Position = point.Position
            };
            flyout.ShowAt(SoundInputOverlay, options);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        SelectSound(sound);

        var now = DateTime.UtcNow;
        var isDoubleClick = string.Equals(_lastSoundRowClickSoundId, sound.Id, StringComparison.Ordinal)
            && (now - _lastSoundRowClickUtc).TotalMilliseconds <= 520;

        _lastSoundRowClickSoundId = sound.Id;
        _lastSoundRowClickUtc = now;

        if (isDoubleClick)
        {
            PlaySelectedSound();
            _lastSoundRowClickSoundId = null;
            _lastSoundRowClickUtc = DateTime.MinValue;
        }

        e.Handled = true;
    }

    private SoundBoardSound? TryGetSoundAtOverlayPoint(Windows.Foundation.Point overlayPoint)
    {
        if (SoundItemsPanel is null || SoundInputOverlay is null)
        {
            return null;
        }

        foreach (var child in SoundItemsPanel.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is not SoundBoardSound sound || child.ActualWidth <= 0 || child.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var childPoint = child.TransformToVisual(SoundInputOverlay).TransformPoint(new Windows.Foundation.Point(0, 0));
                if (overlayPoint.X >= childPoint.X
                    && overlayPoint.X <= childPoint.X + child.ActualWidth
                    && overlayPoint.Y >= childPoint.Y
                    && overlayPoint.Y <= childPoint.Y + child.ActualHeight)
                {
                    return sound;
                }
            }
            catch
            {
                // Ignore transient layout states while rows are being rebuilt.
            }
        }

        return null;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLowLevelHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardLowLevelHookStruct
    {
        public int VkCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private void UpdateBottomPanelVisibility()
    {
        // Gate 5.6: no shared bottom panel. Kept as a no-op for older call sites.
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _manualStopRequested = true;
        SaveCurrentSettings();
        StopEngine(log: false);
        _timelineTimer.Stop();
        _themeReloadTimer.Stop();
        _themeFileWatcher?.Dispose();
        _themeFileWatcher = null;
        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }
        _catalog.Dispose();
    }

    private void OnRefreshDevicesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshDevices();
        }
        catch (Exception ex)
        {
            StartupLog.Write("RefreshDevices button error: " + ex);
            AppendLog($"Device refresh error: {ex.Message}");
        }
    }

    private void RefreshDevices(bool saveAfterRefresh = true)
    {
        _refreshingDevices = true;
        try
        {
            AppendLog("Refreshing audio devices...");
            var captureDevices = _catalog.ListCaptureDevices();
            var renderDevices = _catalog.ListRenderDevices();
            ApplyDeviceLists(captureDevices, renderDevices, saveAfterRefresh);
        }
        finally
        {
            _refreshingDevices = false;
        }
    }

    private void ApplyDeviceLists(IReadOnlyList<AudioDeviceInfo> captureDevices, IReadOnlyList<AudioDeviceInfo> renderDevices, bool saveAfterRefresh)
    {
        var oldInputId = (InputDeviceComboBox.SelectedItem as AudioDeviceInfo)?.Id ?? _settings.InputDeviceId;
        var oldVirtualId = (VirtualOutputComboBox.SelectedItem as AudioDeviceInfo)?.Id ?? _settings.VirtualOutputDeviceId;
        var oldMonitorId = (MonitorOutputComboBox.SelectedItem as AudioDeviceInfo)?.Id ?? _settings.MonitorOutputDeviceId;

        InputDeviceComboBox.ItemsSource = captureDevices;
        VirtualOutputComboBox.ItemsSource = renderDevices;
        MonitorOutputComboBox.ItemsSource = renderDevices;

        var selectedInput = PickById(captureDevices, oldInputId)
            ?? PickByExactName(captureDevices, _settings.InputDeviceName)
            ?? PickByName(captureDevices, _settings.InputDeviceName)
            ?? PickByName(captureDevices, "Fifine")
            ?? captureDevices.FirstOrDefault();

        var detectedVBCable = FindDetectedVBCableRenderDevice(renderDevices);

        var selectedVirtual = PickById(renderDevices, oldVirtualId)
            ?? PickByExactName(renderDevices, _settings.VirtualOutputDeviceName)
            ?? PickByName(renderDevices, _settings.VirtualOutputDeviceName)
            ?? PickByName(renderDevices, "CABLE Input")
            ?? detectedVBCable;

        if (!IsLikelyVBCableDevice(selectedVirtual))
        {
            selectedVirtual = detectedVBCable;
        }

        var selectedMonitor = PickById(renderDevices, oldMonitorId)
            ?? PickByExactName(renderDevices, _settings.MonitorOutputDeviceName)
            ?? PickByName(renderDevices, _settings.MonitorOutputDeviceName)
            ?? PickByName(renderDevices, "Realtek")
            ?? renderDevices.FirstOrDefault();

        InputDeviceComboBox.SelectedItem = selectedInput;
        VirtualOutputComboBox.SelectedItem = selectedVirtual;
        MonitorOutputComboBox.SelectedItem = selectedMonitor;

        AppendLog($"Devices refreshed: {captureDevices.Count} capture, {renderDevices.Count} render.");
        AppendLog($"Selected input: {selectedInput?.FriendlyName ?? "none"}");
        AppendLog($"Selected virtual output: {selectedVirtual?.FriendlyName ?? "none"}");
        AppendLog($"Selected monitor: {selectedMonitor?.FriendlyName ?? "none"}");
        UpdateVBCableUiState();

        if (saveAfterRefresh && !_loadingSettings)
        {
            SaveCurrentSettings();
        }
    }

    private static AudioDeviceInfo? PickByExactName(IReadOnlyList<AudioDeviceInfo> devices, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return devices.FirstOrDefault(d => string.Equals(d.FriendlyName, text, StringComparison.OrdinalIgnoreCase));
    }

    private static AudioDeviceInfo? PickByName(IReadOnlyList<AudioDeviceInfo> devices, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return devices.FirstOrDefault(d => d.FriendlyName.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private static AudioDeviceInfo? PickById(IReadOnlyList<AudioDeviceInfo> devices, string? id)
    {
        return id is null ? null : devices.FirstOrDefault(d => d.Id == id);
    }

    private void LoadSoundBoardLibraryIntoUi()
    {
        _loadingLibrary = true;
        try
        {
            _library = _libraryStore.Load();
            RefreshCategoryControls();

            var selectedCategory = PickCategory(_settings.LastSoundCategoryId)
                ?? PickCategory(_selectedSound?.CategoryId)
                ?? _library.Categories.OrderBy(c => c.SortOrder).FirstOrDefault();

            if (selectedCategory is not null)
            {
                CategoryComboBox.SelectedItem = selectedCategory;
            }

            RefreshSoundList();

            _selectedSound = PickSound(_settings.LastSoundId)
                ?? _library.Sounds.FirstOrDefault(snd => string.Equals(snd.FilePath, _settings.LastSoundFilePath, StringComparison.OrdinalIgnoreCase))
                ?? FilterSoundsForSelectedCategory().FirstOrDefault();

            if (_selectedSound is not null)
            {
                _soundFilePath = _selectedSound.FilePath;
                RefreshSoundList();
            }

            UpdateBottomStats();
            RebuildSceneSoundButtons();
            AppendLog($"SoundBoard library loaded: {_library.Categories.Count} categories, {_library.Sounds.Count} sounds.");
            AppendLog($"SoundBoard data: {_libraryStore.LibraryPath}");
            WarmSoundCacheInBackground();
        }
        catch (Exception ex)
        {
            StartupLog.Write("SoundBoard library UI load error: " + ex);
            AppendLog($"SoundBoard library load error: {ex.Message}");
        }
        finally
        {
            _loadingLibrary = false;
        }
    }

    private void RefreshCategoryControls()
    {
        var categories = _library.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList();
        CategoryComboBox.ItemsSource = categories;
    }

    private void RefreshSoundList()
    {
        _visibleSounds = FilterSoundsForSelectedCategory().ToList();
        if (_selectedSound is not null && _visibleSounds.All(s => s.Id != _selectedSound.Id))
        {
            _selectedSound = null;
            _soundFilePath = null;
        }

        RebuildSoundRows();
        UpdateBottomStats();
    }

    private void RebuildSoundRows()
    {
        if (SoundItemsPanel is null)
        {
            return;
        }

        SoundItemsPanel.Children.Clear();

        foreach (var sound in _visibleSounds)
        {
            SoundItemsPanel.Children.Add(CreateSoundRow(sound));
        }

        UpdateSoundInputOverlayBounds();
    }

    private FrameworkElement CreateSoundRow(SoundBoardSound sound)
    {
        var isSelected = _selectedSound?.Id == sound.Id;
        var row = new Border
        {
            Tag = sound,
            MinHeight = 38,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(isSelected
                ? Microsoft.UI.ColorHelper.FromArgb(0x28, 0xFF, 0xFF, 0xFF)
                : Microsoft.UI.ColorHelper.FromArgb(0x01, 0x00, 0x00, 0x00)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ContextFlyout = CreateSoundContextFlyout(),
            IsTapEnabled = true,
            IsDoubleTapEnabled = true,
            IsRightTapEnabled = true
        };

        var text = new TextBlock
        {
            Text = sound.ListText,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        row.Child = text;
        row.PointerPressed += OnSoundRowPointerPressed;
        row.Tapped += OnSoundRowTapped;
        row.DoubleTapped += OnSoundRowDoubleTapped;
        row.RightTapped += OnSoundRowRightTapped;
        return row;
    }

    private MenuFlyout CreateSoundContextFlyout()
    {
        var flyout = new MenuFlyout();
        var play = new MenuFlyoutItem { Text = "Play" };
        play.Click += OnSoundContextPlayClick;
        var hotkey = new MenuFlyoutItem { Text = "Assign Hotkey" };
        hotkey.Click += OnSoundContextAssignHotkeyClick;
        var rename = new MenuFlyoutItem { Text = "Rename" };
        rename.Click += OnSoundContextRenameClick;
        var edit = new MenuFlyoutItem { Text = "Edit Sound" };
        edit.Click += OnSoundContextEditClick;
        var replace = new MenuFlyoutItem { Text = "Choose Another File" };
        replace.Click += OnSoundContextReplaceFileClick;
        var delete = new MenuFlyoutItem { Text = "Delete From Category" };
        delete.Click += OnSoundContextDeleteClick;

        flyout.Items.Add(play);
        flyout.Items.Add(hotkey);
        flyout.Items.Add(rename);
        flyout.Items.Add(edit);
        flyout.Items.Add(replace);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(delete);
        return flyout;
    }

    private void SelectSound(SoundBoardSound? sound, bool save = true)
    {
        _selectedSound = sound;
        _soundFilePath = sound?.FilePath;
        if (sound is not null)
        {
            _settings.LastSoundId = sound.Id;
            _settings.LastSoundFilePath = sound.FilePath;
        }
        else
        {
            _settings.LastSoundId = null;
            _settings.LastSoundFilePath = null;
        }

        RebuildSoundRows();
        UpdateBottomStats();
        if (save)
        {
            SaveCurrentSettings();
        }
    }

    private IEnumerable<SoundBoardSound> FilterSoundsForSelectedCategory()
    {
        var category = CategoryComboBox.SelectedItem as SoundBoardCategory;
        var query = _library.Sounds.AsEnumerable();
        if (category is not null)
        {
            query = query.Where(s => s.CategoryId == category.Id);
        }

        if (!string.IsNullOrWhiteSpace(_trackSearchText))
        {
            var term = _trackSearchText.Trim();
            query = query.Where(s =>
                s.DisplayName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                s.OriginalFileName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(s.Hotkey) && s.Hotkey.Contains(term, StringComparison.CurrentCultureIgnoreCase)));
        }

        return query.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase);
    }

    private SoundBoardCategory? PickCategory(string? categoryId)
    {
        return string.IsNullOrWhiteSpace(categoryId)
            ? null
            : _library.Categories.FirstOrDefault(c => c.Id == categoryId);
    }

    private SoundBoardSound? PickSound(string? soundId)
    {
        return string.IsNullOrWhiteSpace(soundId)
            ? null
            : _library.Sounds.FirstOrDefault(s => s.Id == soundId);
    }

    private SoundBoardCategory? CurrentCategory => CategoryComboBox.SelectedItem as SoundBoardCategory;

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingLibrary) return;

        var selected = CurrentCategory;
        _settings.LastSoundCategoryId = selected?.Id;
        _selectedSound = null;
        _soundFilePath = null;
        RefreshSoundList();
        SaveCurrentSettings();
    }

    private void OnSoundSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Gate 5.29: the ListView was replaced with a custom overlay scroller.
    }

    private async void OnAddSoundClick(object sender, RoutedEventArgs e)
    {
        var category = CurrentCategory ?? _library.Categories.OrderBy(c => c.SortOrder).FirstOrDefault();
        if (category is null)
        {
            AppendLog("Create a category before adding sounds.");
            return;
        }

        var filePath = await PickSoundFileAsync();
        if (filePath is null) return;

        try
        {
            var sound = _libraryStore.AddSound(_library, filePath, category);
            _selectedSound = sound;
            _soundFilePath = sound.FilePath;
            _settings.LastSoundCategoryId = category.Id;
            _settings.LastSoundId = sound.Id;
            _settings.LastSoundFilePath = sound.FilePath;
            RefreshSoundList();
            SelectSound(sound);
            WarmSoundCacheInBackground(new[] { sound.FilePath });
            AppendLog($"Track added to {category.Name}: {sound.DisplayName}");
        }
        catch (Exception ex)
        {
            AppendLog($"Add track error: {ex.Message}");
        }
    }


    private static bool IsSupportedSoundFile(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
    }

    private void OnSoundBoardDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (SoundBoardDropOverlay is not null && !IsSceneActive)
            {
                SoundBoardDropOverlay.Visibility = Visibility.Visible;
            }
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void OnSoundBoardDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (SoundBoardDropOverlay is not null)
        {
            SoundBoardDropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnSoundBoardDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (SoundBoardDropOverlay is not null)
        {
            SoundBoardDropOverlay.Visibility = Visibility.Collapsed;
        }

        var category = CurrentCategory ?? _library.Categories.OrderBy(c => c.SortOrder).FirstOrDefault();
        if (category is null)
        {
            AppendLog("Create a category before dropping sounds.");
            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var uniquePaths = items
                .OfType<Windows.Storage.StorageFile>()
                .Select(file => file.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path) && IsSupportedSoundFile(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (uniquePaths.Count == 0)
            {
                AppendLog("No supported sound files were dropped. Supported: wav, mp3, ogg.");
                return;
            }

            var signature = category.Id + "|" + string.Join("|", uniquePaths);
            var now = DateTime.UtcNow;
            if (string.Equals(signature, _lastSoundBoardDropSignature, StringComparison.Ordinal)
                && (now - _lastSoundBoardDropUtc).TotalMilliseconds < 1500)
            {
                AppendLog("Duplicate drop event ignored.");
                return;
            }

            _lastSoundBoardDropSignature = signature;
            _lastSoundBoardDropUtc = now;

            var added = 0;
            var addedSoundPaths = new List<string>();
            SoundBoardSound? lastSound = null;
            foreach (var path in uniquePaths)
            {
                lastSound = _libraryStore.AddSound(_library, path, category);
                addedSoundPaths.Add(lastSound.FilePath);
                added++;
            }

            if (added == 0)
            {
                AppendLog("No supported sound files were dropped. Supported: wav, mp3, ogg.");
                return;
            }

            _settings.LastSoundCategoryId = category.Id;
            RefreshSoundList();
            SelectSound(lastSound);
            WarmSoundCacheInBackground(addedSoundPaths);
            AppendLog($"Dropped {added} track(s) into {category.Name}.");
        }
        catch (Exception ex)
        {
            AppendLog($"Drop sound files error: {ex.Message}");
        }
    }

    private async Task<string?> PickSoundFileAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".ogg");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async void OnAddCategoryClick(object sender, RoutedEventArgs e)
    {
        var name = await ShowTextDialogAsync("Create category", "Category name", "New Category");
        if (name is null) return;

        try
        {
            var category = _libraryStore.AddCategory(_library, name);
            RefreshCategoryControls();
            CategoryComboBox.SelectedItem = category;
            SaveCurrentSettings();
            AppendLog($"Category added: {category.Name}");
        }
        catch (Exception ex)
        {
            AppendLog($"Add category error: {ex.Message}");
        }
    }

    private async void OnRenameCategoryClick(object sender, RoutedEventArgs e)
    {
        var category = CurrentCategory;
        if (category is null)
        {
            AppendLog("Select a category to rename.");
            return;
        }

        var name = await ShowTextDialogAsync("Rename category", "Category name", category.Name);
        if (name is null) return;

        _libraryStore.RenameCategory(_library, category, name);
        RefreshCategoryControls();
        CategoryComboBox.SelectedItem = PickCategory(category.Id);
        SaveCurrentSettings();
        AppendLog($"Category renamed: {name}");
    }

    private async void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        var category = CurrentCategory;
        if (category is null)
        {
            AppendLog("Select a category to delete.");
            return;
        }

        var soundCount = _library.Sounds.Count(sound => sound.CategoryId == category.Id);
        var dialog = new ContentDialog
        {
            Title = "Delete category",
            Content = $"Delete category '{category.Name}' and {soundCount} track(s)? Sound files stored in the VoiSe library folder will be removed. This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            AppendLog($"Category delete cancelled: {category.Name}");
            return;
        }

        try
        {
            _libraryStore.DeleteCategory(_library, category, deleteFiles: true);
            _selectedSound = null;
            _soundFilePath = null;
            RefreshCategoryControls();
            CategoryComboBox.SelectedItem = _library.Categories.OrderBy(c => c.SortOrder).FirstOrDefault();
            RefreshSoundList();
            SaveCurrentSettings();
            AppendLog($"Category deleted: {category.Name}");
        }
        catch (Exception ex)
        {
            AppendLog($"Delete category error: {ex.Message}");
        }
    }

    private void OnDeleteSoundClick(object sender, RoutedEventArgs e)
    {
        DeleteSelectedSound();
    }

    private void OnSearchTrackClick(object sender, RoutedEventArgs e)
    {
        ApplyTrackSearch();
    }

    private void OnTrackSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            ApplyTrackSearch();
        }
    }

    private void ApplyTrackSearch()
    {
        _trackSearchText = TrackSearchTextBox?.Text?.Trim() ?? string.Empty;
        RefreshSoundList();
        var first = _visibleSounds.FirstOrDefault();
        SelectSound(first);

        AppendLog(string.IsNullOrWhiteSpace(_trackSearchText)
            ? "Track search cleared."
            : $"Track search: {_trackSearchText}");
    }

    private void DeleteSelectedSound()
    {
        if (_selectedSound is null)
        {
            AppendLog("Select a track to delete.");
            return;
        }

        var deletedName = _selectedSound.DisplayName;
        _libraryStore.DeleteSound(_library, _selectedSound, deleteFile: true);
        _selectedSound = null;
        _soundFilePath = null;
        _settings.LastSoundId = null;
        _settings.LastSoundFilePath = null;
        RefreshSoundList();
        SaveCurrentSettings();
        AppendLog($"Track deleted: {deletedName}");
    }

    private async Task<string?> ShowTextDialogAsync(string title, string placeholder, string value)
    {
        var textBox = new TextBox
        {
            PlaceholderText = placeholder,
            Text = value,
            MinWidth = 320
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text.Trim() : null;
    }

    private void OnSoundListRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Gate 5.29: kept for compatibility with older XAML packages.
    }

    private void OnSoundListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Gate 5.29: kept for compatibility with older XAML packages.
    }

    private void OnSoundRowPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SoundBoardSound sound })
        {
            return;
        }

        var pointer = e.GetCurrentPoint((UIElement)sender);
        if (!pointer.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var isDoubleClick = string.Equals(_lastSoundRowClickSoundId, sound.Id, StringComparison.Ordinal)
            && (now - _lastSoundRowClickUtc).TotalMilliseconds <= 520;

        _lastSoundRowClickSoundId = sound.Id;
        _lastSoundRowClickUtc = now;

        if (isDoubleClick)
        {
            SelectSound(sound);
            PlaySelectedSound();
            _lastSoundRowClickSoundId = null;
            _lastSoundRowClickUtc = DateTime.MinValue;
            e.Handled = true;
        }
    }

    private void OnSoundRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SoundBoardSound sound })
        {
            SelectSound(sound);
        }
    }

    private void OnSoundRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SoundBoardSound sound })
        {
            SelectSound(sound);
        }
    }

    private void OnSoundRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SoundBoardSound sound })
        {
            SelectSound(sound);
            PlaySelectedSound();
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnSoundContextPlayClick(object sender, RoutedEventArgs e) => PlaySelectedSound();

    private async void OnSoundContextAssignHotkeyClick(object sender, RoutedEventArgs e)
    {
        if (_selectedSound is null) return;
        var hotkey = await CaptureHotkeyDialogAsync(
            "Assign sound hotkey",
            "Click the hotkey button, then press a key or Ctrl/Alt/Shift combination. Esc cancels capture. Plain A-Z and < > { } are local-only; NumPad keys and Ctrl/Alt/Shift combinations remain global.",
            _selectedSound.Hotkey);
        if (hotkey is null) return;
        var normalized = NormalizeOptionalHotkey(hotkey);
        _libraryStore.SetHotkey(_library, _selectedSound, normalized);
        RefreshSoundList();
        SaveCurrentSettings();
        AppendLog($"Hotkey assigned: {_selectedSound.DisplayName} -> {normalized ?? "none"}");
    }

    private async void OnSoundContextRenameClick(object sender, RoutedEventArgs e)
    {
        if (_selectedSound is null) return;
        var name = await ShowTextDialogAsync("Rename track", "Display name", _selectedSound.DisplayName);
        if (name is null) return;
        _libraryStore.RenameSound(_library, _selectedSound, name);
        RefreshSoundList();
        RebuildSceneSoundButtons();
        SaveCurrentSettings();
        AppendLog($"Track renamed: {name}");
    }


    private async void OnEditSoundClick(object sender, RoutedEventArgs e)
    {
        await ShowSoundEditorDialogAsync();
    }

    private async void OnSoundContextEditClick(object sender, RoutedEventArgs e)
    {
        await ShowSoundEditorDialogAsync();
    }



    private async Task ShowSoundEditorDialogAsync()
    {
        var sound = _selectedSound;
        if (sound is null)
        {
            AppendLog("Select a track to edit.");
            return;
        }

        if (!File.Exists(sound.FilePath))
        {
            AppendLog("Selected track file was not found.");
            return;
        }

        var originalSourcePath = sound.FilePath;
        var workingSourcePath = originalSourcePath;
        var originalDuration = SoundEditProcessor.GetDurationSeconds(originalSourcePath);
        if (originalDuration <= 0.05)
        {
            AppendLog("Sound editor cannot read this track duration.");
            return;
        }

        var editorDuration = originalDuration;
        var waveformPeaks = await Task.Run(() => BuildSoundEditorWaveform(workingSourcePath));
        const double minimumSelectionSeconds = 0.2;
        const double pointerDragThresholdPixels = 4.0;

        double? selectionStartSeconds = null;
        double? selectionEndSeconds = null;
        var previewPositionSeconds = 0.0;
        var previewRenderOriginSeconds = 0.0;
        var previewReturnSeconds = 0.0;
        var operationBusy = false;
        ContentDialog? activeEditorDialog = null;
        var sessionTempPaths = new List<string>();

        string FormatEditorTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            {
                return "00:00";
            }

            var roundedTenths = Math.Round(seconds, 1);
            var time = TimeSpan.FromSeconds(roundedTenths);
            var showTenths = Math.Abs(roundedTenths - Math.Round(roundedTenths)) > 0.001 || roundedTenths < 1.0;
            if (time.TotalHours >= 1)
            {
                return showTenths
                    ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}"
                    : $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
            }

            return showTenths
                ? $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}"
                : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
        }

        bool HasSelection()
            => selectionStartSeconds.HasValue
               && selectionEndSeconds.HasValue
               && selectionEndSeconds.Value - selectionStartSeconds.Value >= Math.Min(minimumSelectionSeconds, editorDuration) - 0.001;

        double SelectionStart() => selectionStartSeconds ?? 0.0;
        double SelectionEnd() => selectionEndSeconds ?? 0.0;
        double SelectionLength() => HasSelection() ? SelectionEnd() - SelectionStart() : 0.0;

        var title = new TextBlock
        {
            Text = sound.DisplayName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        var originalDurationText = new TextBlock
        {
            Text = $"Original duration: {FormatEditorTime(originalDuration)}",
            Opacity = 0.72,
            VerticalAlignment = VerticalAlignment.Center
        };

        var currentDurationText = new TextBlock
        {
            Text = "Selected duration: —",
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center
        };

        var durationRow = new Grid { ColumnSpacing = 16 };
        durationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        durationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(originalDurationText, 0);
        Grid.SetColumn(currentDurationText, 1);
        durationRow.Children.Add(originalDurationText);
        durationRow.Children.Add(currentDurationText);

        Button CreateToolbarButton(UIElement icon, string toolTip)
        {
            var button = new Button
            {
                Width = 46,
                Height = 40,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = icon
            };
            ToolTipService.SetToolTip(button, toolTip);
            return button;
        }

        UIElement CreateGlyph(string glyph, double fontSize = 20)
            => new TextBlock
            {
                Text = glyph,
                FontSize = fontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

        UIElement CreateSelectionPlayIcon()
        {
            var icon = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.Children.Add(new TextBlock
            {
                Text = "│",
                FontSize = 17,
                Opacity = 0.82,
                VerticalAlignment = VerticalAlignment.Center
            });
            icon.Children.Add(new SymbolIcon(Symbol.Play)
            {
                Width = 18,
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center
            });
            icon.Children.Add(new TextBlock
            {
                Text = "│",
                FontSize = 17,
                Opacity = 0.82,
                VerticalAlignment = VerticalAlignment.Center
            });
            return icon;
        }

        var playFromStartButton = CreateToolbarButton(
            new SymbolIcon(Symbol.Play),
            "Play from the beginning with the current SoundBoard headphones volume");
        var playSelectionButton = CreateToolbarButton(
            CreateSelectionPlayIcon(),
            "Play only the selected fragment from its beginning with the current SoundBoard headphones volume");
        var stopButton = CreateToolbarButton(
            new SymbolIcon(Symbol.Stop),
            "Stop preview and return the playhead to the preview start");
        var trimOutsideButton = CreateToolbarButton(
            new SymbolIcon(Symbol.Crop),
            "Trim outside: keep the selected fragment and remove everything outside it");
        var cutSelectionButton = CreateToolbarButton(
            CreateGlyph("✀", 22),
            "Cut selection: remove the selected fragment and join the remaining parts");
        var resetButton = CreateToolbarButton(
            new SymbolIcon(Symbol.Refresh),
            "Reset all editor changes, volume gain, selection, and playhead");

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                playSelectionButton,
                playFromStartButton,
                stopButton,
                trimOutsideButton,
                cutSelectionButton,
                resetButton
            }
        };

        var waveformRuler = new Canvas
        {
            Height = 28,
            Width = 820,
            MinWidth = 820,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var waveformCanvas = new Canvas
        {
            Height = 208,
            Width = 820,
            MinWidth = 820,
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x1F, 0x1F, 0x1F))
        };

        var waveformBorder = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x1F, 0x1F, 0x1F)),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x3A, 0x3A, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    waveformRuler,
                    waveformCanvas
                }
            }
        };

        var selectionStartText = new TextBlock
        {
            Text = "Selection start: —",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var selectionEndText = new TextBlock
        {
            Text = "Selection end: —",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right
        };

        var selectionTimesRow = new Grid { ColumnSpacing = 16 };
        selectionTimesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selectionTimesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(selectionStartText, 0);
        Grid.SetColumn(selectionEndText, 1);
        selectionTimesRow.Children.Add(selectionStartText);
        selectionTimesRow.Children.Add(selectionEndText);

        var selectionHintText = new TextBlock
        {
            Text = "Drag across the waveform to select a fragment. A single click positions the yellow playhead. Minimum selection: 0.2 s.",
            TextWrapping = TextWrapping.WrapWholeWords,
            Opacity = 0.76,
            MaxWidth = 860
        };

        var gainLabel = new TextBlock
        {
            Text = "Volume gain",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var gainSlider = new Slider
        {
            Minimum = -24,
            Maximum = 12,
            Value = 0,
            StepFrequency = 0.1,
            SmallChange = 0.5,
            LargeChange = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 190
        };

        var gainValueText = new TextBlock
        {
            Width = 92,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        var gainGrid = new Grid
        {
            ColumnSpacing = 10,
            MinWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        gainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        gainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(gainLabel, 0);
        Grid.SetColumn(gainSlider, 1);
        Grid.SetColumn(gainValueText, 2);
        gainGrid.Children.Add(gainLabel);
        gainGrid.Children.Add(gainSlider);
        gainGrid.Children.Add(gainValueText);

        var normalizeToggle = new ToggleSwitch
        {
            Header = "Normalize",
            OffContent = "Off",
            OnContent = "On",
            IsOn = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var fadeInValueText = new TextBlock
        {
            Width = 72,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var fadeInSlider = new Slider
        {
            Minimum = 0,
            Maximum = Math.Max(0.2, editorDuration),
            Value = 0,
            StepFrequency = 0.05,
            SmallChange = 0.05,
            LargeChange = 0.5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 170
        };

        var fadeOutValueText = new TextBlock
        {
            Width = 72,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var fadeOutSlider = new Slider
        {
            Minimum = 0,
            Maximum = Math.Max(0.2, editorDuration),
            Value = 0,
            StepFrequency = 0.05,
            SmallChange = 0.05,
            LargeChange = 0.5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 170
        };

        var distortionValueText = new TextBlock
        {
            Width = 58,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var distortionSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            StepFrequency = 1,
            SmallChange = 1,
            LargeChange = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 170
        };

        Grid CreateEffectSliderRow(string label, Slider slider, TextBlock valueText)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(valueText, 2);
            row.Children.Add(labelBlock);
            row.Children.Add(slider);
            row.Children.Add(valueText);
            return row;
        }

        var leftEffectsColumn = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                gainGrid,
                CreateEffectSliderRow("Fade in", fadeInSlider, fadeInValueText),
                CreateEffectSliderRow("Fade out", fadeOutSlider, fadeOutValueText)
            }
        };

        var rightEffectsColumn = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                normalizeToggle,
                CreateEffectSliderRow("Distortion", distortionSlider, distortionValueText),
                new TextBlock
                {
                    Text = "All effects are non-destructive until Save. The waveform updates immediately.",
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Opacity = 0.68
                }
            }
        };

        var effectsColumns = new Grid { ColumnSpacing = 18 };
        effectsColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        effectsColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftEffectsColumn, 0);
        Grid.SetColumn(rightEffectsColumn, 1);
        effectsColumns.Children.Add(leftEffectsColumn);
        effectsColumns.Children.Add(rightEffectsColumn);

        var effectsPanel = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(14, 12, 14, 12),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Effects",
                        FontSize = 18,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    effectsColumns
                }
            }
        };

        var editorStatusText = new TextBlock
        {
            Text = "Select a fragment or adjust effects. Preview and Save use the waveform shown above.",
            TextWrapping = TextWrapping.WrapWholeWords,
            Opacity = 0.75,
            MaxWidth = 860
        };

        var panel = new StackPanel
        {
            Spacing = 10,
            Width = 860,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                title,
                durationRow,
                toolbar,
                waveformBorder,
                selectionTimesRow,
                effectsPanel,
                selectionHintText,
                editorStatusText
            }
        };

        var contentViewer = new ScrollViewer
        {
            Content = panel,
            Width = 880,
            Height = 650,
            MaxHeight = 650,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Also handle the normal WinUI routed event. The low-level hook above is
        // required for this project because the main SoundBoard uses a calibrated
        // global wheel zone, but this local handler keeps the editor correct even
        // when the global hook is unavailable.
        contentViewer.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler((_, args) =>
            {
                var wheelDelta = args.GetCurrentPoint(contentViewer).Properties.MouseWheelDelta;
                if (wheelDelta == 0)
                {
                    return;
                }

                TryScrollViewer(contentViewer, wheelDelta, 58.0);
                args.Handled = true;
            }),
            true);

        var previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        var pointerMode = "none";
        var rulerDragging = false;
        var pointerPressX = 0.0;
        var selectionAnchorSeconds = 0.0;
        var pointerMoved = false;
        var playheadDragStoppedPreview = false;

        double GetWaveformWidth()
            => Math.Max(1, waveformCanvas.ActualWidth > 1 ? waveformCanvas.ActualWidth : waveformCanvas.Width);

        double ClampToEditor(double value)
            => Math.Clamp(value, 0, Math.Max(0, editorDuration));

        double ClampPlayhead(double value)
            => Math.Clamp(value, 0, Math.Max(0, editorDuration - 0.001));

        double TimeToX(double seconds)
            => editorDuration <= 0.0001 ? 0 : Math.Clamp(seconds / editorDuration, 0, 1) * GetWaveformWidth();

        double XToTime(double x)
            => editorDuration <= 0.0001 ? 0 : Math.Clamp(x / GetWaveformWidth(), 0, 1) * editorDuration;

        float CurrentBoardMonitorVolume()
            => (float)Math.Clamp(SoundMonitorVolumeSlider?.Value ?? 1.0, 0.0, 1.5);

        void UpdateToolbarState()
        {
            var hasSelection = HasSelection();
            playFromStartButton.IsEnabled = !operationBusy;
            playSelectionButton.IsEnabled = !operationBusy && hasSelection;
            stopButton.IsEnabled = !operationBusy;
            trimOutsideButton.IsEnabled = !operationBusy && hasSelection;
            cutSelectionButton.IsEnabled = !operationBusy
                                           && hasSelection
                                           && editorDuration - SelectionLength() >= minimumSelectionSeconds - 0.001;
            resetButton.IsEnabled = !operationBusy;
            gainSlider.IsEnabled = !operationBusy;
            normalizeToggle.IsEnabled = !operationBusy;
            fadeInSlider.IsEnabled = !operationBusy;
            fadeOutSlider.IsEnabled = !operationBusy;
            distortionSlider.IsEnabled = !operationBusy;
            waveformCanvas.IsHitTestVisible = !operationBusy;
            waveformRuler.IsHitTestVisible = !operationBusy;
            if (activeEditorDialog is not null)
            {
                activeEditorDialog.IsPrimaryButtonEnabled = !operationBusy;
                activeEditorDialog.IsSecondaryButtonEnabled = !operationBusy;
                activeEditorDialog.IsEnabled = true;
            }
        }

        void RenderWaveformRuler()
        {
            waveformRuler.Children.Clear();
            var width = Math.Max(1, waveformRuler.ActualWidth > 1 ? waveformRuler.ActualWidth : waveformRuler.Width);
            var majorTickCount = Math.Max(4, Math.Min(12, (int)Math.Ceiling(editorDuration / 20.0) + 4));
            const int minorDivisions = 4;

            for (var majorIndex = 0; majorIndex <= majorTickCount; majorIndex++)
            {
                var ratio = majorIndex / (double)majorTickCount;
                var x = ratio * width;
                waveformRuler.Children.Add(new XamlLine
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 10,
                    Y2 = 24,
                    StrokeThickness = 1,
                    Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xAA, 0xD0, 0xD0, 0xD0))
                });

                var label = new TextBlock
                {
                    Text = FormatEditorTime(editorDuration * ratio),
                    FontSize = 12,
                    Opacity = 0.82
                };
                Canvas.SetLeft(label, Math.Max(0, Math.Min(width - 48, x - 10)));
                Canvas.SetTop(label, 0);
                waveformRuler.Children.Add(label);

                if (majorIndex == majorTickCount)
                {
                    continue;
                }

                for (var minor = 1; minor < minorDivisions; minor++)
                {
                    var minorX = ((majorIndex + minor / (double)minorDivisions) / majorTickCount) * width;
                    waveformRuler.Children.Add(new XamlLine
                    {
                        X1 = minorX,
                        X2 = minorX,
                        Y1 = 16,
                        Y2 = 24,
                        StrokeThickness = 1,
                        Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x66, 0xA0, 0xA0, 0xA0))
                    });
                }
            }
        }

        void RenderWaveform()
        {
            waveformCanvas.Children.Clear();
            var width = GetWaveformWidth();
            var height = Math.Max(1, waveformCanvas.ActualHeight > 1 ? waveformCanvas.ActualHeight : waveformCanvas.Height);
            var centerY = height / 2.0;
            var halfHeight = Math.Max(24, height / 2.0 - 10);
            var peakCount = Math.Max(1, waveformPeaks.Length);
            var columnWidth = width / peakCount;
            var rawPeakMaximum = waveformPeaks.Length == 0 ? 0.0f : waveformPeaks.Max();
            var normalizeScale = normalizeToggle.IsOn && rawPeakMaximum > 0.000001f
                ? 0.98 / rawPeakMaximum
                : 1.0;
            var gainScale = Math.Pow(10.0, Math.Clamp(gainSlider.Value, -48.0, 24.0) / 20.0);
            var distortion = Math.Clamp(distortionSlider.Value / 100.0, 0.0, 1.0);
            var distortionDrive = 1.0 + distortion * 11.0;
            var distortionScale = distortion > 0.0001 ? Math.Tanh(distortionDrive) : 1.0;

            waveformCanvas.Children.Add(new XamlRectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x2A, 0x2A, 0x2A))
            });

            for (var i = 0; i < peakCount; i++)
            {
                var sourceAmplitude = waveformPeaks[Math.Min(i, waveformPeaks.Length - 1)];
                var ratio = peakCount <= 1 ? 0.0 : i / (double)(peakCount - 1);
                var timelineSeconds = ratio * editorDuration;
                var envelope = 1.0;
                if (fadeInSlider.Value > 0.0001 && timelineSeconds < fadeInSlider.Value)
                {
                    envelope *= timelineSeconds / fadeInSlider.Value;
                }
                if (fadeOutSlider.Value > 0.0001)
                {
                    var secondsFromEnd = Math.Max(0, editorDuration - timelineSeconds);
                    if (secondsFromEnd < fadeOutSlider.Value)
                    {
                        envelope *= secondsFromEnd / fadeOutSlider.Value;
                    }
                }

                var effectedAmplitude = sourceAmplitude * normalizeScale * gainScale * envelope;
                if (distortion > 0.0001)
                {
                    effectedAmplitude = Math.Tanh(effectedAmplitude * distortionDrive) / distortionScale;
                }

                var amplitude = Math.Clamp(effectedAmplitude, 0.005, 1.0);
                var barHeight = amplitude * halfHeight;
                var x = i * columnWidth;
                waveformCanvas.Children.Add(new XamlLine
                {
                    X1 = x,
                    X2 = x,
                    Y1 = centerY - barHeight,
                    Y2 = centerY + barHeight,
                    Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xD9, 0xE0, 0xE5)),
                    StrokeThickness = Math.Max(1.0, Math.Min(2.0, columnWidth))
                });
            }

            if (HasSelection())
            {
                var startX = TimeToX(SelectionStart());
                var endX = TimeToX(SelectionEnd());
                var selectionFill = new XamlRectangle
                {
                    Width = Math.Max(2, endX - startX),
                    Height = height,
                    Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x42, 0x36, 0xA9, 0xFF)),
                    Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xE0, 0x36, 0xA9, 0xFF)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(selectionFill, startX);
                waveformCanvas.Children.Add(selectionFill);
            }

            var playheadX = TimeToX(previewPositionSeconds);
            waveformCanvas.Children.Add(new XamlLine
            {
                X1 = playheadX,
                X2 = playheadX,
                Y1 = 0,
                Y2 = height,
                StrokeThickness = 3,
                Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0xC1, 0x07)),
                IsHitTestVisible = false
            });

            var playheadCap = new XamlRectangle
            {
                Width = 14,
                Height = 7,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0xC1, 0x07)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(playheadCap, Math.Max(0, Math.Min(width - playheadCap.Width, playheadX - playheadCap.Width / 2.0)));
            Canvas.SetTop(playheadCap, 0);
            waveformCanvas.Children.Add(playheadCap);
        }

        void UpdateEditorState()
        {
            previewPositionSeconds = ClampPlayhead(previewPositionSeconds);
            var maximumFade = Math.Max(0.2, editorDuration);
            fadeInSlider.Maximum = maximumFade;
            fadeOutSlider.Maximum = maximumFade;
            if (fadeInSlider.Value > maximumFade) fadeInSlider.Value = maximumFade;
            if (fadeOutSlider.Value > maximumFade) fadeOutSlider.Value = maximumFade;
            gainValueText.Text = $"{gainSlider.Value:+0.0;-0.0;0.0} dB";
            fadeInValueText.Text = FormatEditorTime(fadeInSlider.Value);
            fadeOutValueText.Text = FormatEditorTime(fadeOutSlider.Value);
            distortionValueText.Text = $"{distortionSlider.Value:0}%";

            if (HasSelection())
            {
                var selectedDuration = SelectionLength();
                currentDurationText.Text = $"Selected duration: {FormatEditorTime(selectedDuration)}";
                selectionStartText.Text = $"Selection start: {FormatEditorTime(SelectionStart())}";
                selectionEndText.Text = $"Selection end: {FormatEditorTime(SelectionEnd())}";
                selectionHintText.Text = $"Selected: {FormatEditorTime(selectedDuration)}. Use Trim Outside to keep it, or Cut Selection to remove it.";
            }
            else
            {
                currentDurationText.Text = "Selected duration: —";
                selectionStartText.Text = "Selection start: —";
                selectionEndText.Text = "Selection end: —";
                selectionHintText.Text = "Drag across the waveform to select a fragment. A single click positions the yellow playhead. Minimum selection: 0.2 s.";
            }

            UpdateToolbarState();
            RenderWaveformRuler();
            RenderWaveform();
        }

        void StopPreviewCore(bool resetPlayhead, string? statusText = null)
        {
            var status = _engine?.GetSoundStatus(SoundEditorPreviewPlaybackKey) ?? SoundboardStatus.Empty;
            if (status.IsActive)
            {
                previewPositionSeconds = ClampPlayhead(previewRenderOriginSeconds + status.CurrentSeconds);
            }

            _engine?.StopSound(SoundEditorPreviewPlaybackKey);
            previewTimer.Stop();

            if (resetPlayhead)
            {
                previewPositionSeconds = ClampPlayhead(previewReturnSeconds);
            }

            RenderWaveform();
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                editorStatusText.Text = statusText;
            }
        }

        async Task StartPreviewAsync(double startSeconds, double endSeconds, double returnSeconds, string statusText)
        {
            if (_engine is null)
            {
                editorStatusText.Text = "Start the audio engine to preview in headphones.";
                return;
            }

            startSeconds = Math.Clamp(startSeconds, 0, editorDuration);
            endSeconds = Math.Clamp(endSeconds, startSeconds, editorDuration);
            if (endSeconds - startSeconds < 0.01)
            {
                editorStatusText.Text = "The preview range is empty.";
                return;
            }

            try
            {
                operationBusy = true;
                UpdateToolbarState();
                StopPreviewCore(resetPlayhead: false);
                editorStatusText.Text = "Rendering preview...";

                previewRenderOriginSeconds = startSeconds;
                previewReturnSeconds = returnSeconds;
                previewPositionSeconds = startSeconds;

                Directory.CreateDirectory(_libraryStore.EditedSoundsDirectory);
                var previewPath = Path.Combine(
                    _libraryStore.EditedSoundsDirectory,
                    $"preview_{sound.Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.wav");
                sessionTempPaths.Add(previewPath);

                var request = new SoundEditRequest
                {
                    SourcePath = workingSourcePath,
                    TargetPath = previewPath,
                    TrimStartSeconds = startSeconds,
                    TrimEndSeconds = endSeconds,
                    GainDb = gainSlider.Value,
                    Normalize = normalizeToggle.IsOn,
                    FadeInSeconds = fadeInSlider.Value,
                    FadeOutSeconds = fadeOutSlider.Value,
                    DistortionAmount = distortionSlider.Value / 100.0,
                    EffectTimelineOffsetSeconds = startSeconds,
                    EffectTimelineDurationSeconds = editorDuration
                };

                await Task.Run(() => SoundEditProcessor.RenderToWav(request));
                _engine.PlaySound(
                    previewPath,
                    virtualVolume: 0.0f,
                    monitorVolume: CurrentBoardMonitorVolume(),
                    virtualDelayMs: 0,
                    loop: false,
                    playbackKey: SoundEditorPreviewPlaybackKey);

                previewTimer.Start();
                RenderWaveform();
                editorStatusText.Text = statusText;
            }
            catch (Exception ex)
            {
                StopPreviewCore(resetPlayhead: false);
                editorStatusText.Text = $"Preview error: {ex.Message}";
                AppendLog($"Sound editor preview error: {ex.Message}");
            }
            finally
            {
                operationBusy = false;
                UpdateToolbarState();
            }
        }

        async Task HandlePlayPauseHotkeyAsync()
        {
            var status = _engine?.GetSoundStatus(SoundEditorPreviewPlaybackKey) ?? SoundboardStatus.Empty;
            if (!status.IsActive)
            {
                var start = ClampPlayhead(previewPositionSeconds);
                await StartPreviewAsync(
                    start,
                    editorDuration,
                    start,
                    $"Preview started from {FormatEditorTime(start)} at SoundBoard headphones volume ({CurrentBoardMonitorVolume():P0}).");
                return;
            }

            previewPositionSeconds = ClampPlayhead(previewRenderOriginSeconds + status.CurrentSeconds);
            var nowPaused = _engine?.ToggleSoundPause(SoundEditorPreviewPlaybackKey) ?? false;
            if (nowPaused)
            {
                previewTimer.Stop();
                editorStatusText.Text = $"Preview paused at {FormatEditorTime(previewPositionSeconds)}.";
            }
            else
            {
                previewTimer.Start();
                editorStatusText.Text = $"Preview resumed at {FormatEditorTime(previewPositionSeconds)}.";
            }

            RenderWaveform();
        }

        void HandleStop()
            => StopPreviewCore(resetPlayhead: true, "Preview stopped.");

        previewTimer.Tick += (_, _) =>
        {
            var status = _engine?.GetSoundStatus(SoundEditorPreviewPlaybackKey) ?? SoundboardStatus.Empty;
            if (!status.IsActive)
            {
                previewTimer.Stop();
                    previewPositionSeconds = ClampPlayhead(previewReturnSeconds);
                RenderWaveform();
                editorStatusText.Text = "Preview finished.";
                return;
            }

            previewPositionSeconds = ClampPlayhead(previewRenderOriginSeconds + status.CurrentSeconds);
            RenderWaveform();
        };

        void ApplySelectionDrag(double currentSeconds)
        {
            var minLength = Math.Min(minimumSelectionSeconds, editorDuration);
            var start = Math.Min(selectionAnchorSeconds, currentSeconds);
            var end = Math.Max(selectionAnchorSeconds, currentSeconds);

            if (end - start < minLength)
            {
                if (currentSeconds >= selectionAnchorSeconds)
                {
                    end = Math.Min(editorDuration, selectionAnchorSeconds + minLength);
                    start = Math.Max(0, end - minLength);
                }
                else
                {
                    start = Math.Max(0, selectionAnchorSeconds - minLength);
                    end = Math.Min(editorDuration, start + minLength);
                }
            }

            selectionStartSeconds = start;
            selectionEndSeconds = end;
            UpdateEditorState();
        }

        waveformRuler.PointerPressed += (_, e) =>
        {
            StopPreviewCore(resetPlayhead: false);
            rulerDragging = true;
            waveformRuler.CapturePointer(e.Pointer);
            previewPositionSeconds = ClampPlayhead(XToTime(e.GetCurrentPoint(waveformRuler).Position.X));
            RenderWaveform();
            e.Handled = true;
        };

        waveformRuler.PointerMoved += (_, e) =>
        {
            if (!rulerDragging)
            {
                return;
            }

            previewPositionSeconds = ClampPlayhead(XToTime(e.GetCurrentPoint(waveformRuler).Position.X));
            RenderWaveform();
            e.Handled = true;
        };

        waveformRuler.PointerReleased += (_, e) =>
        {
            if (!rulerDragging)
            {
                return;
            }

            rulerDragging = false;
            waveformRuler.ReleasePointerCaptures();
            editorStatusText.Text = $"Preview position: {FormatEditorTime(previewPositionSeconds)}.";
            e.Handled = true;
        };
        waveformRuler.PointerCanceled += (_, _) =>
        {
            rulerDragging = false;
            waveformRuler.ReleasePointerCaptures();
        };
        waveformRuler.PointerCaptureLost += (_, _) => rulerDragging = false;

        waveformCanvas.PointerPressed += (_, e) =>
        {
            var x = e.GetCurrentPoint(waveformCanvas).Position.X;
            var playheadX = TimeToX(previewPositionSeconds);
            const double playheadHitWidth = 9.0;

            pointerPressX = x;
            selectionAnchorSeconds = ClampToEditor(XToTime(x));
            pointerMoved = false;
            playheadDragStoppedPreview = false;
            pointerMode = Math.Abs(x - playheadX) <= playheadHitWidth ? "playhead" : "pending";
            waveformCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        };

        waveformCanvas.PointerMoved += (_, e) =>
        {
            if (pointerMode == "none")
            {
                return;
            }

            var x = e.GetCurrentPoint(waveformCanvas).Position.X;
            var seconds = ClampToEditor(XToTime(x));

            if (pointerMode == "playhead")
            {
                if (!playheadDragStoppedPreview)
                {
                    StopPreviewCore(resetPlayhead: false);
                    playheadDragStoppedPreview = true;
                }

                pointerMoved = true;
                previewPositionSeconds = ClampPlayhead(seconds);
                RenderWaveform();
                e.Handled = true;
                return;
            }

            if (pointerMode == "pending" && Math.Abs(x - pointerPressX) >= pointerDragThresholdPixels)
            {
                pointerMode = "selection";
                pointerMoved = true;
            }

            if (pointerMode == "selection")
            {
                ApplySelectionDrag(seconds);
            }

            e.Handled = true;
        };

        void EndWaveformPointer(PointerRoutedEventArgs e)
        {
            if (pointerMode == "none")
            {
                return;
            }

            var releaseSeconds = ClampToEditor(XToTime(e.GetCurrentPoint(waveformCanvas).Position.X));
            if (pointerMode == "pending" && !pointerMoved)
            {
                StopPreviewCore(resetPlayhead: false);
                previewPositionSeconds = ClampPlayhead(releaseSeconds);
                editorStatusText.Text = $"Preview position: {FormatEditorTime(previewPositionSeconds)}.";
            }
            else if (pointerMode == "selection")
            {
                ApplySelectionDrag(releaseSeconds);
                editorStatusText.Text = $"Selected {FormatEditorTime(SelectionLength())}. The playhead position was preserved.";
            }
            else if (pointerMode == "playhead")
            {
                previewPositionSeconds = ClampPlayhead(releaseSeconds);
                editorStatusText.Text = $"Preview position: {FormatEditorTime(previewPositionSeconds)}.";
            }

            pointerMode = "none";
            waveformCanvas.ReleasePointerCaptures();
            UpdateEditorState();
            e.Handled = true;
        }

        waveformCanvas.PointerReleased += (_, e) => EndWaveformPointer(e);
        waveformCanvas.PointerCanceled += (_, e) => EndWaveformPointer(e);
        waveformCanvas.PointerCaptureLost += (_, _) => pointerMode = "none";

        void OnEffectValueChanged()
        {
            StopPreviewCore(resetPlayhead: false);
            UpdateEditorState();
        }

        gainSlider.ValueChanged += (_, _) => OnEffectValueChanged();
        normalizeToggle.Toggled += (_, _) => OnEffectValueChanged();
        fadeInSlider.ValueChanged += (_, _) => OnEffectValueChanged();
        fadeOutSlider.ValueChanged += (_, _) => OnEffectValueChanged();
        distortionSlider.ValueChanged += (_, _) => OnEffectValueChanged();
        waveformCanvas.SizeChanged += (_, _) => UpdateEditorState();
        waveformRuler.SizeChanged += (_, _) => UpdateEditorState();

        async Task RefreshWorkingWaveformAsync(string statusText, double nextPlayhead)
        {
            editorDuration = SoundEditProcessor.GetDurationSeconds(workingSourcePath);
            waveformPeaks = await Task.Run(() => BuildSoundEditorWaveform(workingSourcePath));
            selectionStartSeconds = null;
            selectionEndSeconds = null;
            previewPositionSeconds = ClampPlayhead(nextPlayhead);
            previewReturnSeconds = previewPositionSeconds;
            UpdateEditorState();
            editorStatusText.Text = statusText;
        }

        async Task TrimOutsideSelectionAsync()
        {
            if (!HasSelection() || operationBusy)
            {
                return;
            }

            try
            {
                operationBusy = true;
                UpdateToolbarState();
                StopPreviewCore(resetPlayhead: false);
                editorStatusText.Text = "Trimming outside the selection...";
                Directory.CreateDirectory(_libraryStore.EditedSoundsDirectory);
                var targetPath = Path.Combine(
                    _libraryStore.EditedSoundsDirectory,
                    $"edit_trim_{sound.Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.wav");
                sessionTempPaths.Add(targetPath);

                var request = new SoundEditRequest
                {
                    SourcePath = workingSourcePath,
                    TargetPath = targetPath,
                    TrimStartSeconds = SelectionStart(),
                    TrimEndSeconds = SelectionEnd(),
                    GainDb = 0
                };

                await Task.Run(() => SoundEditProcessor.RenderToWav(request));
                workingSourcePath = targetPath;
                await RefreshWorkingWaveformAsync("Everything outside the selection was removed.", 0);
            }
            catch (Exception ex)
            {
                editorStatusText.Text = $"Trim error: {ex.Message}";
                AppendLog($"Sound editor trim error: {ex.Message}");
            }
            finally
            {
                operationBusy = false;
                UpdateToolbarState();
            }
        }

        async Task CutSelectionAsync()
        {
            if (!HasSelection() || operationBusy)
            {
                return;
            }

            if (editorDuration - SelectionLength() < minimumSelectionSeconds - 0.001)
            {
                editorStatusText.Text = "Cut blocked: at least 0.2 seconds must remain in the sound.";
                return;
            }

            var cutStart = SelectionStart();
            try
            {
                operationBusy = true;
                UpdateToolbarState();
                StopPreviewCore(resetPlayhead: false);
                editorStatusText.Text = "Cutting the selected fragment...";
                Directory.CreateDirectory(_libraryStore.EditedSoundsDirectory);
                var targetPath = Path.Combine(
                    _libraryStore.EditedSoundsDirectory,
                    $"edit_cut_{sound.Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.wav");
                sessionTempPaths.Add(targetPath);

                var request = new SoundCutRequest
                {
                    SourcePath = workingSourcePath,
                    TargetPath = targetPath,
                    CutStartSeconds = SelectionStart(),
                    CutEndSeconds = SelectionEnd(),
                    GainDb = 0
                };

                await Task.Run(() => SoundEditProcessor.RenderCutToWav(request));
                workingSourcePath = targetPath;
                await RefreshWorkingWaveformAsync("The selected fragment was removed and the remaining parts were joined.", cutStart);
            }
            catch (Exception ex)
            {
                editorStatusText.Text = $"Cut error: {ex.Message}";
                AppendLog($"Sound editor cut error: {ex.Message}");
            }
            finally
            {
                operationBusy = false;
                UpdateToolbarState();
            }
        }

        playFromStartButton.Click += async (_, _) =>
            await StartPreviewAsync(
                0,
                editorDuration,
                0,
                $"Preview started from the beginning at SoundBoard headphones volume ({CurrentBoardMonitorVolume():P0}).");

        playSelectionButton.Click += async (_, _) =>
        {
            if (!HasSelection())
            {
                editorStatusText.Text = "Select a fragment first.";
                return;
            }

            await StartPreviewAsync(
                SelectionStart(),
                SelectionEnd(),
                SelectionStart(),
                $"Preview started from the selection start at SoundBoard headphones volume ({CurrentBoardMonitorVolume():P0}).");
        };

        stopButton.Click += (_, _) => HandleStop();
        trimOutsideButton.Click += async (_, _) => await TrimOutsideSelectionAsync();
        cutSelectionButton.Click += async (_, _) => await CutSelectionAsync();
        resetButton.Click += async (_, _) =>
        {
            if (operationBusy)
            {
                return;
            }

            try
            {
                operationBusy = true;
                UpdateToolbarState();
                StopPreviewCore(resetPlayhead: false);
                workingSourcePath = originalSourcePath;
                editorDuration = originalDuration;
                waveformPeaks = await Task.Run(() => BuildSoundEditorWaveform(workingSourcePath));
                selectionStartSeconds = null;
                selectionEndSeconds = null;
                gainSlider.Value = 0;
                normalizeToggle.IsOn = false;
                fadeInSlider.Value = 0;
                fadeOutSlider.Value = 0;
                distortionSlider.Value = 0;
                previewPositionSeconds = 0;
                previewReturnSeconds = 0;
                UpdateEditorState();
                editorStatusText.Text = "All unsaved editor changes were reset.";
            }
            catch (Exception ex)
            {
                editorStatusText.Text = $"Reset error: {ex.Message}";
                AppendLog($"Sound editor reset error: {ex.Message}");
            }
            finally
            {
                operationBusy = false;
                UpdateToolbarState();
            }
        };

        UpdateEditorState();

        var dialog = new ContentDialog
        {
            Title = "Sound Editor",
            Content = contentViewer,
            PrimaryButtonText = "Save File",
            SecondaryButtonText = "Save as",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            MinWidth = 940,
            MaxWidth = 940,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        activeEditorDialog = dialog;
        UpdateToolbarState();
        dialog.Resources["ContentDialogMinWidth"] = 940d;
        dialog.Resources["ContentDialogMaxWidth"] = 940d;
        void CenterEditorDialog()
        {
            if (dialog.XamlRoot?.Content is not FrameworkElement rootElement
                || rootElement.ActualWidth <= 0
                || rootElement.ActualHeight <= 0
                || dialog.ActualWidth <= 0
                || dialog.ActualHeight <= 0)
            {
                return;
            }

            dialog.RenderTransform = null;
            var currentTopLeft = dialog.TransformToVisual(rootElement)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            var targetX = Math.Max(0, (rootElement.ActualWidth - dialog.ActualWidth) / 2.0);
            var targetY = Math.Max(0, (rootElement.ActualHeight - dialog.ActualHeight) / 2.0);
            dialog.RenderTransform = new TranslateTransform
            {
                X = targetX - currentTopLeft.X,
                Y = targetY - currentTopLeft.Y
            };
        }

        dialog.Loaded += (_, _) => dialog.DispatcherQueue.TryEnqueue(CenterEditorDialog);
        dialog.SizeChanged += (_, _) => dialog.DispatcherQueue.TryEnqueue(CenterEditorDialog);

        async Task<string> RenderSavedOutputAsync(string prefix)
        {
            Directory.CreateDirectory(_libraryStore.EditedSoundsDirectory);
            var renderedPath = Path.Combine(
                _libraryStore.EditedSoundsDirectory,
                $"{prefix}_{sound.Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.wav");
            sessionTempPaths.Add(renderedPath);
            var request = new SoundEditRequest
            {
                SourcePath = workingSourcePath,
                TargetPath = renderedPath,
                TrimStartSeconds = 0,
                TrimEndSeconds = editorDuration,
                GainDb = gainSlider.Value,
                Normalize = normalizeToggle.IsOn,
                FadeInSeconds = fadeInSlider.Value,
                FadeOutSeconds = fadeOutSlider.Value,
                DistortionAmount = distortionSlider.Value / 100.0,
                EffectTimelineOffsetSeconds = 0,
                EffectTimelineDurationSeconds = editorDuration
            };
            await Task.Run(() => SoundEditProcessor.RenderToWav(request));
            return renderedPath;
        }

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                StopPreviewCore(resetPlayhead: false);
                editorStatusText.Text = "Saving edited file...";
                args.Cancel = true;
                var renderedPath = await RenderSavedOutputAsync("save");
                await SaveEditedSoundAsync(sound, renderedPath, saveAsCopy: false);
                args.Cancel = false;
            }
            catch (Exception ex)
            {
                editorStatusText.Text = $"Save error: {ex.Message}";
                AppendLog($"Sound editor save error: {ex.Message}");
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        dialog.SecondaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                StopPreviewCore(resetPlayhead: false);
                editorStatusText.Text = "Saving edited file as a new [edit] copy...";
                args.Cancel = true;
                var renderedPath = await RenderSavedOutputAsync("copy");
                await SaveEditedSoundAsync(sound, renderedPath, saveAsCopy: true);
                args.Cancel = false;
            }
            catch (Exception ex)
            {
                editorStatusText.Text = $"Save as error: {ex.Message}";
                AppendLog($"Sound editor Save as error: {ex.Message}");
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        _engine?.StopSound();
        if (_pushToTalkPreviousVoicePreset is not null)
        {
            ApplyVoicePreset(_pushToTalkPreviousVoicePreset);
            _pushToTalkPreviousVoicePreset = null;
        }
        _activePushToTalkGesture = null;
        UpdateTimeline();
        _suppressSoundBoardTimelineForEditorPreview = true;
        _activeSoundEditorScrollViewer = contentViewer;
        _soundEditorActive = true;
        _soundEditorPlayPauseAction = () => _ = HandlePlayPauseHotkeyAsync();
        _soundEditorStopAction = HandleStop;

        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            StopPreviewCore(resetPlayhead: false);
            activeEditorDialog = null;
            _soundEditorPlayPauseAction = null;
            _soundEditorStopAction = null;
            _soundEditorActive = false;
            _activeSoundEditorScrollViewer = null;
            _suppressSoundBoardTimelineForEditorPreview = false;
            UpdateTimeline();

            foreach (var tempPath in sessionTempPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    SoundFileLoader.Invalidate(tempPath);
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Editor cleanup is best-effort and must never break closing.
                }
            }
        }

        AppendLog(result == ContentDialogResult.None
            ? "Sound editor closed."
            : $"Sound editor finished: {sound.DisplayName}.");
    }

    private static float[] BuildSoundEditorWaveform(string filePath, int requestedPeaks = 900)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new[] { 0.05f };
        }

        try
        {
            using WaveStream reader = Path.GetExtension(filePath).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                ? new NAudio.Vorbis.VorbisWaveReader(filePath)
                : new NAudio.Wave.AudioFileReader(filePath);

            var sampleProvider = reader.ToSampleProvider();
            var channels = Math.Max(1, sampleProvider.WaveFormat.Channels);
            var totalFrames = Math.Max(1L, (long)Math.Round(reader.TotalTime.TotalSeconds * sampleProvider.WaveFormat.SampleRate));
            var framesPerBucket = Math.Max(256, (int)Math.Ceiling(totalFrames / (double)Math.Max(64, requestedPeaks)));
            var sourceBuffer = new float[Math.Max(4096, sampleProvider.WaveFormat.SampleRate / 2 * channels)];
            var peaks = new List<float>(requestedPeaks);
            var currentPeak = 0f;
            var framesInBucket = 0;

            int read;
            while ((read = sampleProvider.Read(sourceBuffer, 0, sourceBuffer.Length)) > 0)
            {
                for (var sampleIndex = 0; sampleIndex < read; sampleIndex += channels)
                {
                    var framePeak = 0f;
                    for (var channel = 0; channel < channels && sampleIndex + channel < read; channel++)
                    {
                        framePeak = Math.Max(framePeak, Math.Abs(sourceBuffer[sampleIndex + channel]));
                    }

                    currentPeak = Math.Max(currentPeak, framePeak);
                    framesInBucket++;

                    if (framesInBucket >= framesPerBucket)
                    {
                        peaks.Add(currentPeak);
                        currentPeak = 0f;
                        framesInBucket = 0;
                    }
                }
            }

            if (framesInBucket > 0 || peaks.Count == 0)
            {
                peaks.Add(currentPeak);
            }

            return peaks.Count == 0
                ? new[] { 0.0f }
                : peaks.Select(peak => Math.Clamp(peak, 0.0f, 1.0f)).ToArray();
        }
        catch
        {
            return new[] { 0.05f };
        }
    }

    private static float[] NormalizeWaveformPeaks(IReadOnlyList<float> peaks)
    {
        if (peaks.Count == 0)
        {
            return Array.Empty<float>();
        }

        var max = peaks.Max();
        if (max <= 0.0001f)
        {
            max = 1f;
        }

        var result = new float[peaks.Count];
        for (var i = 0; i < peaks.Count; i++)
        {
            result[i] = Math.Clamp(peaks[i] / max, 0.02f, 1.0f);
        }

        return result;
    }

    private async Task SaveEditedSoundAsync(SoundBoardSound sourceSound, string renderedPath, bool saveAsCopy)
    {
        var oldPath = sourceSound.FilePath;
        SoundBoardSound selectedAfterSave;

        if (saveAsCopy)
        {
            selectedAfterSave = _libraryStore.AddEditedCopy(_library, sourceSound, renderedPath);
            AppendLog($"Edited copy created: {selectedAfterSave.DisplayName}");
        }
        else
        {
            _engine?.StopSound();
            _libraryStore.ReplaceSoundWithEditedWav(_library, sourceSound, renderedPath);
            selectedAfterSave = sourceSound;
            AppendLog($"Track edited: {selectedAfterSave.DisplayName}");
        }

        SoundFileLoader.Invalidate(oldPath);
        SoundFileLoader.Invalidate(selectedAfterSave.FilePath);
        _soundDurationSecondsCache.Remove(oldPath);
        _soundDurationSecondsCache.Remove(selectedAfterSave.FilePath);

        _selectedSound = selectedAfterSave;
        _soundFilePath = selectedAfterSave.FilePath;
        _settings.LastSoundId = selectedAfterSave.Id;
        _settings.LastSoundFilePath = selectedAfterSave.FilePath;
        _settings.LastSoundCategoryId = selectedAfterSave.CategoryId;
        RefreshSoundList();
        SelectSound(selectedAfterSave);
        RebuildSceneSoundButtons();
        UpdateSceneTimelines();
        SaveCurrentSettings();
        WarmSoundCacheInBackground(new[] { selectedAfterSave.FilePath });

        await Task.CompletedTask;
    }

    private async void OnSoundContextReplaceFileClick(object sender, RoutedEventArgs e)
    {
        if (_selectedSound is null) return;
        var path = await PickSoundFileAsync();
        if (path is null) return;
        try
        {
            _libraryStore.ReplaceSoundFile(_library, _selectedSound, path);
            _soundFilePath = _selectedSound.FilePath;
            RefreshSoundList();
            SaveCurrentSettings();
            AppendLog($"Track file replaced: {_selectedSound.DisplayName}");
        }
        catch (Exception ex)
        {
            AppendLog($"Replace file error: {ex.Message}");
        }
    }

    private void OnSoundContextDeleteClick(object sender, RoutedEventArgs e) => DeleteSelectedSound();

    private void OnStartEngineClick(object sender, RoutedEventArgs e)
    {
        _manualStopRequested = false;
        StartEngine(logAlreadyRunning: true);
    }

    private void OnStopEngineClick(object sender, RoutedEventArgs e)
    {
        _manualStopRequested = true;
        StopEngine(log: true);
    }

    private bool StartEngine(bool logAlreadyRunning)
    {
        if (_engine is not null)
        {
            if (logAlreadyRunning) AppendLog("Engine is already running.");
            return true;
        }

        var inputInfo = InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        var virtualInfo = VirtualOutputComboBox.SelectedItem as AudioDeviceInfo;
        var monitorInfo = MonitorOutputComboBox.SelectedItem as AudioDeviceInfo;

        if (inputInfo is null || virtualInfo is null)
        {
            AppendLog("Select input microphone and virtual output first.");
            UpdateVBCableUiState();
            return false;
        }

        if (!IsLikelyVBCableDevice(virtualInfo))
        {
            AppendLog("VB-CABLE was not detected. Install VB-CABLE before starting the engine.");
            UpdateVBCableUiState();
            return false;
        }

        var input = _catalog.FindCaptureDevice(inputInfo.Id);
        var virtualOutput = _catalog.FindRenderDevice(virtualInfo.Id);
        var monitor = monitorInfo is null ? null : _catalog.FindRenderDevice(monitorInfo.Id);

        if (input is null || virtualOutput is null)
        {
            AppendLog("Selected devices are not available anymore. Refresh devices and try again.");
            return false;
        }

        try
        {
            SaveCurrentSettings();
            _engine = new Gate2UnifiedAudioEngine(input, virtualOutput, monitor, CreateEffectSettings());
            _engine.Start();
            _engine.SetVirtualMicMuted(_virtualMicMuted);
            EngineStatusTextBlock.Text = "Running";
            AppendLog($"Engine started. Input: {input.FriendlyName}");
            AppendLog($"Virtual output: {virtualOutput.FriendlyName}");
            AppendLog($"Monitor: {(monitor is null ? "disabled" : monitor.FriendlyName)}");
            WarmSoundCacheInBackground();
            return true;
        }
        catch (Exception ex)
        {
            _engine?.Dispose();
            _engine = null;
            EngineStatusTextBlock.Text = "Error";
            AppendLog($"Engine start error: {ex.Message}");
            return false;
        }
    }

    private void StopEngine(bool log)
    {
        if (_engine is null) return;

        try
        {
            _engine.Dispose();
            if (log) AppendLog("Engine stopped.");
        }
        catch (Exception ex)
        {
            AppendLog($"Engine stop error: {ex.Message}");
        }
        finally
        {
            _engine = null;
            EngineStatusTextBlock.Text = "Stopped";
            UpdateVBCableUiState();
        }
    }

    private void OnVirtualMicMuteToggleClick(object sender, RoutedEventArgs e)
    {
        ToggleVirtualMicMute("UI");
    }

    private void ToggleVirtualMicMute(string source)
    {
        _virtualMicMuted = !_virtualMicMuted;
        ApplyVirtualMicMuteState(playCue: true);
        AppendLog($"Virtual mic output {(_virtualMicMuted ? "muted" : "live")} ({source}).");
    }

    private void ApplyVirtualMicMuteState(bool playCue)
    {
        _engine?.SetVirtualMicMuted(_virtualMicMuted);
        UpdateVirtualMicMuteUi();

        if (playCue)
        {
            PlayVirtualMicMuteCue(_virtualMicMuted);
        }
    }

    private void UpdateVirtualMicMuteUi()
    {
        if (VirtualMicMuteStatusTextBlock is not null)
        {
            VirtualMicMuteStatusTextBlock.Text = _virtualMicMuted ? "Muted" : "Live";
            VirtualMicMuteStatusTextBlock.Foreground = new SolidColorBrush(_virtualMicMuted
                ? Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0x4D, 0x4D)
                : Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x37, 0xD6, 0x7A));
        }

        if (VirtualMicMuteToggleButton is not null)
        {
            VirtualMicMuteToggleButton.Content = _virtualMicMuted ? "Unmute" : "Mute";
        }

        if (VirtualMicMutedBanner is not null)
        {
            VirtualMicMutedBanner.Visibility = _virtualMicMuted ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void PlayVirtualMicMuteCue(bool muted)
    {
        var engine = _engine;
        if (engine is null)
        {
            return;
        }

        var cuePath = Path.Combine(AppContext.BaseDirectory, muted ? MuteOnCueRelativePath : MuteOffCueRelativePath);
        if (!File.Exists(cuePath))
        {
            AppendLog($"Virtual mic mute cue not found: {cuePath}");
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                engine.PlaySound(cuePath, 0.0f, 0.45f, 0, loop: false, playbackKey: "__voisee_virtual_mic_mute_cue");
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => AppendLog($"Virtual mic mute cue error: {ex.Message}"));
            }
        });
    }

    private void RestartEngine(string reason)
    {
        if (_engine is null || _manualStopRequested) return;
        AppendLog($"Engine auto-restart: {reason}.");
        StopEngine(log: false);
        StartEngine(logAlreadyRunning: false);
    }

    private void OnRouteRestartTimerTick(object? sender, object e)
    {
        _routeRestartTimer.Stop();
        RestartEngine(_pendingRestartReason);
    }

    private void ScheduleEngineRestart(string reason)
    {
        if (_engine is null || _refreshingDevices || _loadingSettings) return;
        _pendingRestartReason = reason;
        _routeRestartTimer.Stop();
        _routeRestartTimer.Start();
        EngineStatusTextBlock.Text = "Restart pending";
    }

    private void ApplyLiveSettings(string reason)
    {
        UpdateAllLabels();
        SaveCurrentSettings();

        if (_engine is null) return;

        _engine.UpdateEffectSettings(CreateEffectSettings());
        AppendLog($"Live settings applied: {reason}.");
    }

    private EffectSettings CreateEffectSettings()
    {
        var compressorValue = GetVoiceValue(CompressorThresholdSlider, CompressorThresholdValueBox);

        return new EffectSettings
        {
            InputGainDb = 0.0f,
            VoiceGainDb = (float)MapCentered(GetVoiceValue(VoiceGainSlider, VoiceGainValueBox), 0, -24, 18),
            GateThresholdDb = (float)MapCentered(GetVoiceValue(GateThresholdSlider, GateThresholdValueBox), -45, -80, -15),
            CompressorThresholdDb = (float)MapCentered(compressorValue, -24, -60, -6),
            CompressorRatio = (float)MapCentered(compressorValue, 4, 1.5, 16),
            PitchSemitones = ToPitchSemitones(GetVoiceValue(PitchSlider, PitchValueBox)),
            FormantShiftSemitones = ToFormantSemitones(GetVoiceValue(FormantSlider, FormantValueBox)),
            BassAmount = ToEffectAmount(GetVoiceValue(BassSlider, BassValueBox)),
            TrebleAmount = ToEffectAmount(GetVoiceValue(TrebleSlider, TrebleValueBox)),
            DistortionAmount = ToEffectAmount(GetVoiceValue(DistortionSlider, DistortionValueBox)),
            RobotAmount = ToEffectAmount(GetVoiceValue(RobotSlider, RobotValueBox)),
            TremoloAmount = ToEffectAmount(GetVoiceValue(TremoloSlider, TremoloValueBox)),
            EchoAmount = ToEffectAmount(GetVoiceValue(EchoSlider, EchoValueBox)),
            ReverbAmount = ToEffectAmount(GetVoiceValue(ReverbSlider, ReverbValueBox)),
            RadioAmount = ToEffectAmount(GetVoiceValue(RadioSlider, RadioValueBox)),
            BitCrusherAmount = ToEffectAmount(GetVoiceValue(BitCrusherSlider, BitCrusherValueBox)),
            AlienAmount = ToEffectAmount(GetVoiceValue(AlienSlider, AlienValueBox)),
            GateEnabled = true,
            CompressorEnabled = true,
            LimiterEnabled = true,
            LimiterCeilingDb = -1.0f,
            VirtualOutputGain = (float)VirtualOutputVolumeSlider.Value,
            VoiceMonitorGain = _voiceMonitorEnabled ? 1.0f : 0.0f
        };
    }

    private static float ToPitchSemitones(double value)
    {
        // Slider -100..+100 maps to +/-12 semitones. Numeric boxes may go
        // further, but the real-time shifter is clamped to +/-24 semitones.
        return (float)Clamp(value / 100.0 * 12.0, -24.0, 24.0);
    }

    private static float ToFormantSemitones(double value)
    {
        // Formant is intentionally separate from Bass/Treble: it shifts the vocal
        // resonance model up/down while Pitch changes perceived note height.
        return (float)Clamp(value / 100.0 * 12.0, -24.0, 24.0);
    }

    private static float ToEffectAmount(double value)
    {
        // The slider range maps to -1..+1. Numeric fields may intentionally exceed it,
        // but the DSP clamps to keep the real-time audio path stable.
        return (float)Clamp(value / 100.0, -4.0, 4.0);
    }

    private static double MapCentered(double normalized, double center, double min, double max)
    {
        normalized = Clamp(normalized, -100, 100);
        return normalized < 0
            ? center + (normalized / 100.0) * (center - min)
            : center + (normalized / 100.0) * (max - center);
    }


    private void OnSoundLoopToggleChanged(object sender, RoutedEventArgs e)
    {
        if (IsSceneActive)
        {
            UpdateSoundBoardSceneLockState();
            AppendLog("SoundBoard loop toggle is unavailable while a scene is active.");
            return;
        }

        _soundBoardLoopEnabled = SoundLoopToggleButton?.IsChecked == true;
        _engine?.UpdateSoundLoop(_soundBoardLoopEnabled);

        AppendLog(_soundBoardLoopEnabled
            ? "SoundBoard loop enabled for the current track."
            : "SoundBoard loop disabled.");
    }

    private void OnPlaySoundClick(object sender, RoutedEventArgs e)
    {
        if (BlockSoundBoardPlaybackIfSceneActive()) return;
        PlaySelectedSound();
    }

    private void OnPlayPauseSoundClick(object sender, RoutedEventArgs e)
    {
        if (BlockSoundBoardPlaybackIfSceneActive()) return;
        var status = _engine?.GetSoundStatus() ?? SoundboardStatus.Empty;
        if (!status.IsActive)
        {
            PlaySelectedSound();
            return;
        }

        var paused = _engine?.ToggleSoundPause() ?? false;
        UpdateTimeline();
        AppendLog(paused ? "Sound paused." : "Sound resumed.");
    }

    private void PlaySelectedSound(bool loop = false)
    {
        if (BlockSoundBoardPlaybackIfSceneActive()) return;
        loop = loop || _soundBoardLoopEnabled;
        var soundPath = _selectedSound?.FilePath ?? _soundFilePath;
        var displayName = _selectedSound?.DisplayName ?? (string.IsNullOrWhiteSpace(soundPath) ? "Sound" : System.IO.Path.GetFileName(soundPath));
        PlaySoundPath(
            soundPath,
            _selectedSound,
            displayName,
            loop,
            SoundVirtualVolumeSlider?.Value ?? 1.0,
            SoundMonitorVolumeSlider?.Value ?? 1.0,
            "Sound");
    }

    private void PlaySoundBoardSound(SoundBoardSound sound, bool loop, double virtualVolume, double monitorVolume, string sourceLabel)
    {
        SelectSound(sound);
        PlaySoundPath(sound.FilePath, sound, sound.DisplayName, loop, virtualVolume, monitorVolume, sourceLabel);
    }

    private void PlaySceneSound(VoiSeScene scene, SceneSoundButton sceneButton, SoundBoardSound sound, bool loop, string sourceLabel)
    {
        var playbackKey = loop
            ? SceneLoopPlaybackKey(scene.Id, sceneButton.Id)
            : SceneButtonPlaybackKey(scene.Id, sceneButton.Id);
        var virtualVolume = loop ? scene.LoopedSoundVirtualMicVolume : sceneButton.VirtualMicVolume;
        var monitorVolume = loop ? scene.LoopedSoundHeadphonesVolume : sceneButton.HeadphonesVolume;
        var displayName = loop || string.IsNullOrWhiteSpace(sceneButton.LocalName)
            ? sound.DisplayName
            : sceneButton.LocalName!.Trim();
        PlaySoundPath(sound.FilePath, sound, displayName, loop, virtualVolume, monitorVolume, sourceLabel, playbackKey, updateSoundBoardTransportUi: false);
        UpdateSceneTimelines();
    }

    private void PlaySoundPath(
        string? soundPath,
        SoundBoardSound? librarySound,
        string? displayName,
        bool loop,
        double virtualVolume,
        double monitorVolume,
        string sourceLabel,
        string? playbackKey = null,
        bool updateSoundBoardTransportUi = true)
    {
        if (_engine is null)
        {
            AppendLog("Start engine before playing sound.");
            return;
        }

        if (string.IsNullOrWhiteSpace(soundPath) || !File.Exists(soundPath))
        {
            AppendLog("Choose an existing track from the SoundBoard library first.");
            return;
        }

        try
        {
            SaveCurrentSettings();
            var delayMs = (int)Math.Round(SoundVirtualDelaySlider.Value);
            var virtualRouteVolume = (float)Clamp(virtualVolume, 0, 1.5);
            var monitorRouteVolume = (float)Clamp(monitorVolume, 0, 1.5);
            var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? System.IO.Path.GetFileNameWithoutExtension(soundPath)
                : displayName!.Trim();
            if (updateSoundBoardTransportUi)
            {
                _currentSoundDisplayName = normalizedDisplayName;
            }

            var engine = _engine;
            var logMessage = $"{sourceLabel} started{(loop ? " in loop" : string.Empty)}: {normalizedDisplayName}. HP: {(int)Math.Round(monitorRouteVolume * 100)}%, Mic: {(int)Math.Round(virtualRouteVolume * 100)}%, Virtual delay: {delayMs} ms.";

            // Decoding/resampling can be expensive and used to freeze the pointer/UI for
            // ~0.5-1s on larger files. Keep the UI thread free; the transport is thread-safe
            // and SoundFileLoader caches the decoded PCM for future starts.
            _ = Task.Run(() => engine.PlaySound(soundPath, virtualRouteVolume, monitorRouteVolume, delayMs, loop, playbackKey))
                .ContinueWith(task =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (task.Exception is not null)
                        {
                            AppendLog($"Sound playback error: {task.Exception.GetBaseException().Message}");
                            return;
                        }

                        if (librarySound is not null)
                        {
                            _libraryStore.IncrementUsage(_library, librarySound);
                        }

                        UpdateBottomStats();
                        AppendLog(logMessage);
                    });
                }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            AppendLog($"Sound playback error: {ex.Message}");
        }
    }

    private void OnStopSoundClick(object sender, RoutedEventArgs e)
    {
        if (BlockSoundBoardPlaybackIfSceneActive()) return;
        _engine?.StopSound();
        _currentSoundDisplayName = null;
        UpdateTimeline();
        AppendLog("Sound stopped.");
    }


    private void OnPreviousSoundClick(object sender, RoutedEventArgs e)
    {
        if (BlockSoundBoardPlaybackIfSceneActive()) return;
        SelectRelativeSound(-1, play: true);
    }

    private void OnNextSoundClick(object sender, RoutedEventArgs e)
    {
        if (BlockSoundBoardPlaybackIfSceneActive()) return;
        SelectRelativeSound(1, play: true);
    }

    private void SelectRelativeSound(int delta, bool play)
    {
        if (play && BlockSoundBoardPlaybackIfSceneActive()) return;
        var sounds = FilterSoundsForSelectedCategory().ToList();
        if (sounds.Count == 0) return;

        var index = _selectedSound is null ? -1 : sounds.FindIndex(s => s.Id == _selectedSound.Id);
        var nextIndex = index < 0 ? 0 : (index + delta + sounds.Count) % sounds.Count;
        SelectSound(sounds[nextIndex]);
        if (play) PlaySelectedSound();
    }

    private void OnRouteSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingDevices || _loadingSettings) return;
        UpdateVBCableUiState();
        SaveCurrentSettings();
        ScheduleEngineRestart("audio route changed");
    }

    private void OnDelayChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateDelayLabel();
        if (!_loadingSettings) SaveCurrentSettings();
    }

    private void OnSoundVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateSoundVolumeLabels();
        if (_loadingSettings) return;

        SaveCurrentSettings();
        if (_engine is not null && !IsSceneActive)
        {
            _engine.UpdateSoundVolumes((float)SoundVirtualVolumeSlider.Value, (float)SoundMonitorVolumeSlider.Value);
        }
    }

    private void OnSceneVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateSceneVolumeLabels();
        if (_loadingSceneUi || _selectedScene is null)
        {
            return;
        }

        _selectedScene.LoopedSoundHeadphonesVolume = SceneLoopHeadphonesVolumeSlider?.Value ?? 1.0;
        _selectedScene.LoopedSoundVirtualMicVolume = SceneLoopVirtualMicVolumeSlider?.Value ?? 1.0;
        var loopedButton = GetSelectedSceneLoopedButton();
        if (loopedButton is not null && _engine is not null)
        {
            _engine.UpdateSoundVolumes(
                (float)_selectedScene.LoopedSoundVirtualMicVolume,
                (float)_selectedScene.LoopedSoundHeadphonesVolume,
                SceneLoopPlaybackKey(_selectedScene.Id, loopedButton.Id));
        }
        SaveSelectedSceneVolumeChange();
    }

    private void SaveSelectedSceneVolumeChange()
    {
        if (_selectedScene is null || _loadingSceneUi)
        {
            return;
        }

        try
        {
            _sceneStore.OverwriteScene(_selectedScene);
        }
        catch (Exception ex)
        {
            AppendLog($"Scene volume save error: {ex.Message}");
        }
    }

    private void OnOutputVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateOutputVolumeLabels();
        if (!_loadingSettings) ApplyLiveSettings("master output volume changed");
    }

    private void OnVoiceSettingsChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncingVoiceControls)
        {
            return;
        }

        if (sender is Slider slider)
        {
            _syncingVoiceControls = true;
            try
            {
                SyncVoiceTextBoxFromSlider(slider);
            }
            finally
            {
                _syncingVoiceControls = false;
            }
        }

        UpdateVoiceSettingLabels();
        if (!_loadingVoicePreset && !_loadingSettings)
        {
            _lastAppliedVoicePresetName = null;
        }
        ScheduleVoiceSettingsApply();
    }

    private void OnVoiceValueTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingVoiceControls || sender is not TextBox textBox)
        {
            return;
        }

        if (!TryParseVoiceValue(textBox.Text, out var value))
        {
            return;
        }

        _syncingVoiceControls = true;
        try
        {
            SyncVoiceSliderFromTextBox(textBox, value);
        }
        finally
        {
            _syncingVoiceControls = false;
        }

        UpdateVoiceSettingLabels();
        ScheduleVoiceSettingsApply();
    }

    private void ScheduleVoiceSettingsApply()
    {
        if (_loadingSettings || _loadingVoicePreset)
        {
            return;
        }

        _voiceSettingsApplyTimer.Stop();
        _voiceSettingsApplyTimer.Start();
    }

    private void OnVoiceSettingsApplyTimerTick(object? sender, object e)
    {
        _voiceSettingsApplyTimer.Stop();
        ApplyLiveSettings("voice setting changed");
    }

    private void OnToggleVoiceMonitorClick(object sender, RoutedEventArgs e)
    {
        _voiceMonitorEnabled = !_voiceMonitorEnabled;
        UpdateVoiceMonitorButton();
        ApplyLiveSettings(_voiceMonitorEnabled ? "voice monitor enabled" : "voice monitor disabled");
    }

    private void OnTimelineHostPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (BlockSoundBoardPlaybackIfSceneActive()) return;
        var status = _engine?.GetSoundStatus() ?? SoundboardStatus.Empty;
        if (!status.IsActive)
        {
            return;
        }

        _timelineUserDragging = true;
        TimelineHost.CapturePointer(e.Pointer);
        SeekSoundToPointer(e);
    }

    private void OnTimelineHostPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_timelineUserDragging)
        {
            return;
        }

        SeekSoundToPointer(e);
    }

    private void OnTimelineHostPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_timelineUserDragging)
        {
            return;
        }

        SeekSoundToPointer(e);
        _timelineUserDragging = false;
        TimelineHost.ReleasePointerCapture(e.Pointer);
        UpdateTimeline();
    }

    private void OnTimelineHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTimeline();

    private void SeekSoundToPointer(PointerRoutedEventArgs e)
    {
        if (_engine is null || TimelineHost.ActualWidth <= 1)
        {
            return;
        }

        var point = e.GetCurrentPoint(TimelineHost).Position;
        var ratio = Math.Max(0, Math.Min(1, point.X / TimelineHost.ActualWidth));
        var seconds = ratio * _timelineMaximumSeconds;
        _engine.SeekSound(seconds);
        SetTimelineVisual(seconds, _timelineMaximumSeconds);
        CurrentTimeTextBlock.Text = FormatTime(seconds);
    }

    private void OnTimelineTimerTick(object? sender, object e)
    {
        UpdateTimeline();
        UpdateSceneTimelines();
    }

    private void UpdateTimeline()
    {
        if (TimelineHost is null)
        {
            return;
        }

        // Sound Editor preview is an isolated overlay. It must never drive the
        // main SoundBoard transport timeline while the dialog is previewing.
        if (_suppressSoundBoardTimelineForEditorPreview)
        {
            return;
        }

        UpdateSoundBoardSceneLockState();
        if (IsSceneActive)
        {
            _timelineMaximumSeconds = 1;
            if (!_timelineUserDragging)
            {
                SetTimelineVisual(0, 1);
                CurrentTimeTextBlock.Text = "00:00";
            }

            TotalTimeTextBlock.Text = "00:00";
            TransportStatusTextBlock.Text = "Scene active — SoundBoard locked";
            PlayPauseButton.Content = "";
            TimelineHost.Opacity = 0.28;
            return;
        }

        var status = _engine?.GetSoundStatus() ?? SoundboardStatus.Empty;
        var max = Math.Max(1, status.DurationSeconds);
        _timelineMaximumSeconds = max;

        if (!_timelineUserDragging)
        {
            SetTimelineVisual(status.CurrentSeconds, max);
            CurrentTimeTextBlock.Text = FormatTime(status.CurrentSeconds);
        }

        TotalTimeTextBlock.Text = FormatTime(status.DurationSeconds);
        if (status.IsActive)
        {
            var title = string.IsNullOrWhiteSpace(_currentSoundDisplayName) ? "sound" : _currentSoundDisplayName;
            TransportStatusTextBlock.Text = status.IsPaused
                ? $"paused: {title}"
                : $"playing: {title}";
        }
        else
        {
            TransportStatusTextBlock.Text = "No sound";
        }
        PlayPauseButton.Content = status.IsActive && !status.IsPaused ? "\uE769" : "\uE768";
        TimelineHost.Opacity = status.IsActive ? 1.0 : 0.45;
    }

    private void SetTimelineVisual(double currentSeconds, double durationSeconds)
    {
        if (TimelineHost is null || TimelineFill is null || TimelineThumbTransform is null)
        {
            return;
        }

        var width = Math.Max(0, TimelineHost.ActualWidth);
        var duration = Math.Max(1, durationSeconds);
        var ratio = Math.Max(0, Math.Min(1, currentSeconds / duration));
        var fillWidth = width * ratio;
        TimelineFill.Width = fillWidth;
        TimelineThumbTransform.X = Math.Max(0, Math.Min(width - 16, fillWidth - 8));
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"mm\:ss");
    }


    private void LoadScenesIntoUi()
    {
        try
        {
            var selectedId = _selectedScene?.Id;
            _scenes = _sceneStore.LoadScenes();
            UpdateSceneActiveFlags();
            if (ScenesListView is not null)
            {
                var nextSelection = _scenes.FirstOrDefault(s => s.Id == selectedId) ?? _scenes.FirstOrDefault();
                ScenesListView.ItemsSource = _scenes;
                ScenesListView.SelectedItem = nextSelection;
                _selectedScene = nextSelection;
            }

            UpdateSceneDetails();
            AppendLog($"Scenes loaded: {_scenes.Count}. Folder: {_sceneStore.ScenesDirectory}");
        }
        catch (Exception ex)
        {
            AppendLog($"Scenes load error: {ex.Message}");
        }
    }

    private void OnSceneSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedScene = ScenesListView?.SelectedItem as VoiSeScene;
        UpdateSceneDetails();
    }

    private void UpdateSceneDetails()
    {
        var hasScene = _selectedScene is not null;
        if (SceneApplyButton is not null) SceneApplyButton.IsEnabled = hasScene;
        if (SceneRenameButton is not null) SceneRenameButton.IsEnabled = hasScene;
        if (SceneDeleteButton is not null) SceneDeleteButton.IsEnabled = hasScene;
        SetSceneEditorEnabled(hasScene);

        if (SceneDetailsTitleTextBlock is not null)
        {
            SceneDetailsTitleTextBlock.Text = _selectedScene?.Name ?? "Scene details";
        }

        RefreshSceneVoicePresetComboBox();
        RefreshSceneLoopAutostartCheckBox();
        RefreshSceneVolumeControls();
        RebuildSceneSoundButtons();
    }

    private void SetSceneEditorEnabled(bool enabled)
    {
        if (SceneVoicePresetComboBox is not null) SceneVoicePresetComboBox.IsEnabled = enabled;
        if (SceneVoicePresetClearButton is not null) SceneVoicePresetClearButton.IsEnabled = enabled;
        if (SceneVoicePresetCreateButton is not null) SceneVoicePresetCreateButton.IsEnabled = enabled;
        if (SceneVoiceMonitorButton is not null) SceneVoiceMonitorButton.IsEnabled = enabled;
        if (SceneAutostartLoopsCheckBox is not null) SceneAutostartLoopsCheckBox.IsEnabled = enabled;
        if (SceneLoopHeadphonesVolumeSlider is not null) SceneLoopHeadphonesVolumeSlider.IsEnabled = enabled;
        if (SceneLoopVirtualMicVolumeSlider is not null) SceneLoopVirtualMicVolumeSlider.IsEnabled = enabled;
        RefreshSceneLoopActionButtons();
    }

    private void RefreshSceneVoicePresetComboBox()
    {
        if (SceneVoicePresetComboBox is null)
        {
            return;
        }

        _loadingSceneUi = true;
        try
        {
            var selectedName = _selectedScene?.VoicePresetName;
            var presets = _voicePresets.ToList();
            SceneVoicePresetComboBox.ItemsSource = presets;
            SceneVoicePresetComboBox.SelectedItem = string.IsNullOrWhiteSpace(selectedName)
                ? null
                : presets.FirstOrDefault(p => string.Equals(p.Name, selectedName, StringComparison.CurrentCultureIgnoreCase));
        }
        finally
        {
            _loadingSceneUi = false;
        }
    }

    private void RefreshSceneLoopAutostartCheckBox()
    {
        if (SceneAutostartLoopsCheckBox is null)
        {
            return;
        }

        _loadingSceneUi = true;
        try
        {
            SceneAutostartLoopsCheckBox.IsChecked = _selectedScene?.AutoStartLoopedSounds ?? false;
        }
        finally
        {
            _loadingSceneUi = false;
        }
    }

    private void RefreshSceneVolumeControls()
    {
        if (SceneLoopHeadphonesVolumeSlider is null ||
            SceneLoopVirtualMicVolumeSlider is null)
        {
            return;
        }

        _loadingSceneUi = true;
        try
        {
            SceneLoopHeadphonesVolumeSlider.Value = Clamp(_selectedScene?.LoopedSoundHeadphonesVolume ?? 1.0, 0, 1.5);
            SceneLoopVirtualMicVolumeSlider.Value = Clamp(_selectedScene?.LoopedSoundVirtualMicVolume ?? 1.0, 0, 1.5);
        }
        finally
        {
            _loadingSceneUi = false;
        }

        UpdateSceneVolumeLabels();
    }

    private string CreateSceneDetailsText(VoiSeScene scene)
    {
        var normalCount = scene.SoundButtons.Count(b => !b.IsLooped);
        var hasLoop = scene.SoundButtons.Any(b => b.IsLooped);
        return string.Join(Environment.NewLine,
            $"Voice preset: {scene.VoicePresetName ?? "none"}",
            $"Scene buttons: {normalCount}",
            $"Looped sound: {(hasLoop ? "set" : "none")}",
            $"Autostart loop: {(scene.AutoStartLoopedSounds ? "on" : "off")}",
            $"Looped → Virtual Mic: {scene.LoopedSoundVirtualMicVolume:P0}",
            $"Looped → Headphones: {scene.LoopedSoundHeadphonesVolume:P0}",
            $"Virtual Mic Master: {scene.VirtualMicMasterVolume:P0}",
            $"SoundBoard → Virtual Mic: {scene.SoundBoardVirtualMicVolume:P0}",
            $"SoundBoard → Headphones: {scene.SoundBoardHeadphonesVolume:P0}",
            $"SoundBoard Delay: {scene.SoundBoardVirtualMicDelayMs:0} ms",
            $"Updated UTC: {scene.UpdatedAtUtc:yyyy-MM-dd HH:mm:ss}");
    }

    private async void OnCreateNewSceneClick(object sender, RoutedEventArgs e)
    {
        var name = await ShowTextDialogAsync("Create new scene", "Scene name", "New Scene");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var scene = CaptureCurrentScene(name.Trim());
            scene.VoicePresetName = null;
            scene.VoiceSliders.Clear();
            scene.SoundCategoryId = null;
            scene.SoundCategoryName = null;
            scene.BackgroundSoundId = null;
            scene.BackgroundSoundName = null;
            scene.SoundButtons.Clear();
            scene.AutoStartLoopedSounds = false;
            scene.LoopedSoundVirtualMicVolume = 1.0;
            scene.LoopedSoundHeadphonesVolume = 1.0;
            scene.SceneButtonsVirtualMicVolume = 1.0;
            scene.SceneButtonsHeadphonesVolume = 1.0;
            scene.SoundBoardVirtualMicVolume = 1.0;
            scene.SoundBoardHeadphonesVolume = 1.0;
            _sceneStore.SaveScene(scene);
            _selectedScene = scene;
            LoadScenesIntoUi();
            SelectSceneById(scene.Id);
            AppendLog($"Scene created: {scene.Name}");
        }
        catch (Exception ex)
        {
            AppendLog($"Scene create error: {ex.Message}");
        }
    }

    private void OnDisableScenesClick(object sender, RoutedEventArgs e)
    {
        DisableActiveScene("Scenes disabled.");
    }

    private void DisableActiveScene(string logMessage)
    {
        _activeSceneId = null;
        UpdateSceneActiveFlags();
        RefreshSceneListBinding();
        TransportStop();
        RestoreVoiceChangerStateBeforeScene();
        AppendLog(logMessage);
    }

    private void OnApplySceneClick(object sender, RoutedEventArgs e)
    {
        if (_selectedScene is null)
        {
            AppendLog("Select a scene to apply.");
            return;
        }

        ApplyScene(_selectedScene);
    }

    private void OnUpdateSceneClick(object sender, RoutedEventArgs e)
    {
        if (_selectedScene is null)
        {
            AppendLog("Select a scene to update.");
            return;
        }

        try
        {
            var previousButtons = _selectedScene.SoundButtons
                .Select(CloneSceneSoundButton)
                .ToList();
            var previousAutostartLoops = _selectedScene.AutoStartLoopedSounds;
            var previousLoopVirtualVolume = _selectedScene.LoopedSoundVirtualMicVolume;
            var previousLoopHeadphonesVolume = _selectedScene.LoopedSoundHeadphonesVolume;
            var updated = CaptureCurrentScene(_selectedScene.Name);
            updated.Id = _selectedScene.Id;
            updated.Icon = _selectedScene.Icon;
            updated.CreatedAtUtc = _selectedScene.CreatedAtUtc;
            updated.FilePath = _selectedScene.FilePath;
            updated.SoundButtons = previousButtons.Count == 0 ? updated.SoundButtons : previousButtons;
            updated.AutoStartLoopedSounds = previousAutostartLoops;
            updated.LoopedSoundVirtualMicVolume = previousLoopVirtualVolume;
            updated.LoopedSoundHeadphonesVolume = previousLoopHeadphonesVolume;
            _sceneStore.OverwriteScene(updated);
            _selectedScene = updated;
            LoadScenesIntoUi();
            SelectSceneById(updated.Id);
            AppendLog($"Scene updated from current state: {updated.Name}");
        }
        catch (Exception ex)
        {
            AppendLog($"Scene update error: {ex.Message}");
        }
    }

    private async void OnRenameSceneClick(object sender, RoutedEventArgs e)
    {
        if (_selectedScene is null)
        {
            AppendLog("Select a scene to rename.");
            return;
        }

        var newName = await ShowTextDialogAsync("Rename scene", "Scene name", _selectedScene.Name);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        try
        {
            var id = _selectedScene.Id;
            _sceneStore.RenameScene(_selectedScene, newName.Trim());
            LoadScenesIntoUi();
            SelectSceneById(id);
            AppendLog($"Scene renamed: {newName.Trim()}");
        }
        catch (Exception ex)
        {
            AppendLog($"Scene rename error: {ex.Message}");
        }
    }

    private async void OnDeleteSceneClick(object sender, RoutedEventArgs e)
    {
        if (_selectedScene is null)
        {
            AppendLog("Select a scene to delete.");
            return;
        }

        var scene = _selectedScene;
        var dialog = new ContentDialog
        {
            Title = "Delete scene",
            Content = $"Delete scene '{scene.Name}'? The JSON file will be removed from the scenes folder.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            _sceneStore.DeleteScene(scene);
            if (string.Equals(_activeSceneId, scene.Id, StringComparison.OrdinalIgnoreCase))
            {
                _activeSceneId = null;
                RestoreVoiceChangerStateBeforeScene();
            }

            _selectedScene = null;
            LoadScenesIntoUi();
            AppendLog($"Scene deleted: {scene.Name}");
        }
        catch (Exception ex)
        {
            AppendLog($"Scene delete error: {ex.Message}");
        }
    }

    private void OnOpenScenesFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_sceneStore.ScenesDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _sceneStore.ScenesDirectory,
                UseShellExecute = true
            });
            AppendLog($"Scenes folder opened: {_sceneStore.ScenesDirectory}");
        }
        catch (Exception ex)
        {
            AppendLog($"Open scenes folder error: {ex.Message}");
        }
    }

    private VoiSeScene CaptureCurrentScene(string name)
    {
        var category = CurrentCategory;
        var sound = _selectedSound;
        var sceneSoundButtons = new List<SceneSoundButton>();
        if (sound is not null)
        {
            sceneSoundButtons.Add(new SceneSoundButton
            {
                SoundId = sound.Id,
                LocalName = sound.DisplayName,
                VirtualMicVolume = SoundVirtualVolumeSlider?.Value ?? 1.0,
                HeadphonesVolume = SoundMonitorVolumeSlider?.Value ?? 1.0,
                IsLooped = false,
                SortOrder = 0
            });
        }

        return new VoiSeScene
        {
            Name = name,
            VoicePresetName = _lastAppliedVoicePresetName,
            VoiceSliders = CaptureCurrentVoicePreset(name).Sliders,
            VoiceMonitorEnabled = false,
            SoundCategoryId = category?.Id,
            SoundCategoryName = category?.Name,
            BackgroundSoundId = sound?.Id,
            BackgroundSoundName = sound?.DisplayName,
            SoundButtons = sceneSoundButtons,
            LoopedSoundVirtualMicVolume = SoundVirtualVolumeSlider?.Value ?? 1.0,
            LoopedSoundHeadphonesVolume = SoundMonitorVolumeSlider?.Value ?? 1.0,
            SceneButtonsVirtualMicVolume = SoundVirtualVolumeSlider?.Value ?? 1.0,
            SceneButtonsHeadphonesVolume = SoundMonitorVolumeSlider?.Value ?? 1.0,
            VirtualMicMasterVolume = VirtualOutputVolumeSlider?.Value ?? 1.0,
            SoundBoardVirtualMicVolume = SoundVirtualVolumeSlider?.Value ?? 1.0,
            SoundBoardHeadphonesVolume = SoundMonitorVolumeSlider?.Value ?? 1.0,
            SoundBoardVirtualMicDelayMs = SoundVirtualDelaySlider?.Value ?? 85.0
        };
    }

    private void ApplyScene(VoiSeScene scene)
    {
        try
        {
            CaptureVoiceChangerStateBeforeSceneIfNeeded();
            VirtualOutputVolumeSlider.Value = Clamp(scene.VirtualMicMasterVolume, 0, 1.5);
            SoundVirtualVolumeSlider.Value = Clamp(scene.SoundBoardVirtualMicVolume, 0, 1.5);
            SoundMonitorVolumeSlider.Value = Clamp(scene.SoundBoardHeadphonesVolume, 0, 1.5);
            SoundVirtualDelaySlider.Value = Clamp(scene.SoundBoardVirtualMicDelayMs, 0, 300);
            var scenePreset = string.IsNullOrWhiteSpace(scene.VoicePresetName)
                ? null
                : _voicePresets.FirstOrDefault(p => string.Equals(p.Name, scene.VoicePresetName, StringComparison.CurrentCultureIgnoreCase));
            if (scenePreset is not null)
            {
                ApplyVoicePreset(scenePreset);
            }
            else
            {
                ApplyVoiceSliderDictionary(scene.VoiceSliders);
                _lastAppliedVoicePresetName = null;
            }

            // Applying a scene must not disturb the user's current SoundBoard category or selected sound.
            // Scene playback uses SoundId lookups directly and is intentionally independent from SoundBoard browsing state.
            UpdateAllLabels();
            ApplyLiveSettings($"scene applied: {scene.Name}");
            if (_engine is not null)
            {
                _engine.UpdateSoundVolumes((float)SoundVirtualVolumeSlider.Value, (float)SoundMonitorVolumeSlider.Value);
            }

            _activeSceneId = scene.Id;
            UpdateSceneActiveFlags();
            RefreshSceneListBinding();

            if (scene.AutoStartLoopedSounds)
            {
                _engine?.StopSound();
                var loopedButton = scene.SoundButtons
                    .Where(b => b.IsLooped)
                    .OrderBy(b => b.SortOrder)
                    .FirstOrDefault();
                var loopedSound = loopedButton is null ? null : PickSound(loopedButton.SoundId);
                if (loopedButton is not null && loopedSound is not null)
                {
                    PlaySceneSound(scene, loopedButton, loopedSound, true, "Scene looped sound");
                    AppendLog("Looped sound autostart requested. Scene looped sound started in loop mode.");
                }
            }

            AppendLog($"Scene applied: {scene.Name}");
        }
        catch (Exception ex)
        {
            AppendLog($"Scene apply error: {ex.Message}");
        }
    }

    private void CaptureVoiceChangerStateBeforeSceneIfNeeded()
    {
        if (!string.IsNullOrWhiteSpace(_activeSceneId) || _voicePresetBeforeActiveScene is not null)
        {
            return;
        }

        _voicePresetBeforeActiveScene = CaptureCurrentVoicePreset("Before active scene");
        _lastAppliedVoicePresetNameBeforeActiveScene = _lastAppliedVoicePresetName;
    }

    private void RestoreVoiceChangerStateBeforeScene()
    {
        if (_voicePresetBeforeActiveScene is null)
        {
            return;
        }

        var previous = _voicePresetBeforeActiveScene;
        var previousPresetName = _lastAppliedVoicePresetNameBeforeActiveScene;
        _voicePresetBeforeActiveScene = null;
        _lastAppliedVoicePresetNameBeforeActiveScene = null;

        ApplyVoiceSliderDictionary(previous.Sliders);
        UpdateVoiceSettingLabels();
        _lastAppliedVoicePresetName = previousPresetName;
        ApplyLiveSettings("voice settings restored after scene disabled");
    }

    private void ApplyVoiceSliderDictionary(IReadOnlyDictionary<string, double>? sliders)
    {
        if (sliders is null)
        {
            return;
        }

        _loadingVoicePreset = true;
        try
        {
            SetVoiceControlFromDictionary(VoiceGainSlider, VoiceGainValueBox, sliders, "VoiceGain");
            SetVoiceControlFromDictionary(GateThresholdSlider, GateThresholdValueBox, sliders, "Gate");
            SetVoiceControlFromDictionary(CompressorThresholdSlider, CompressorThresholdValueBox, sliders, "Compressor");
            SetVoiceControlFromDictionary(PitchSlider, PitchValueBox, sliders, "Pitch");
            SetVoiceControlFromDictionary(FormantSlider, FormantValueBox, sliders, "Formant");
            SetVoiceControlFromDictionary(BassSlider, BassValueBox, sliders, "Bass");
            SetVoiceControlFromDictionary(TrebleSlider, TrebleValueBox, sliders, "Treble");
            SetVoiceControlFromDictionary(DistortionSlider, DistortionValueBox, sliders, "Distortion");
            SetVoiceControlFromDictionary(RobotSlider, RobotValueBox, sliders, "Robot");
            SetVoiceControlFromDictionary(TremoloSlider, TremoloValueBox, sliders, "Tremolo");
            SetVoiceControlFromDictionary(EchoSlider, EchoValueBox, sliders, "Echo");
            SetVoiceControlFromDictionary(ReverbSlider, ReverbValueBox, sliders, "Reverb");
            SetVoiceControlFromDictionary(RadioSlider, RadioValueBox, sliders, "Radio");
            SetVoiceControlFromDictionary(BitCrusherSlider, BitCrusherValueBox, sliders, "BitCrusher");
            SetVoiceControlFromDictionary(AlienSlider, AlienValueBox, sliders, "Alien");
        }
        finally
        {
            _loadingVoicePreset = false;
        }
    }

    private void SetVoiceControlFromDictionary(Slider slider, TextBox textBox, IReadOnlyDictionary<string, double> sliders, string key)
    {
        if (sliders.TryGetValue(key, out var value))
        {
            SetVoiceControl(slider, textBox, value);
        }
    }


    private bool IsSceneActive => !string.IsNullOrWhiteSpace(_activeSceneId);

    private bool IsSceneActiveForPlayback(VoiSeScene? scene)
    {
        return scene is not null
            && !string.IsNullOrWhiteSpace(_activeSceneId)
            && string.Equals(scene.Id, _activeSceneId, StringComparison.OrdinalIgnoreCase);
    }

    private bool RequireSceneActiveForPlayback(VoiSeScene? scene, string action)
    {
        if (IsSceneActiveForPlayback(scene))
        {
            return true;
        }

        AppendLog($"Scene must be active to {action}.");
        return false;
    }

    private void UpdateScenePlaybackInteractivity()
    {
        RefreshSceneLoopActionButtons();
        foreach (var binding in _sceneTimelineBindings.Values)
        {
            binding.Slider.IsEnabled = IsSelectedSceneActiveForPlayback();
        }
    }

    private bool IsSelectedSceneActiveForPlayback() => IsSceneActiveForPlayback(_selectedScene);

    private void UpdateSceneActiveFlags()
    {
        foreach (var scene in _scenes)
        {
            scene.IsActive = !string.IsNullOrWhiteSpace(_activeSceneId)
                && string.Equals(scene.Id, _activeSceneId, StringComparison.OrdinalIgnoreCase);
        }

        UpdateSoundBoardSceneLockState();
    }

    private void UpdateSoundBoardSceneLockState()
    {
        var locked = IsSceneActive;
        if (PreviousSoundButton is not null) PreviousSoundButton.IsEnabled = !locked;
        if (NextSoundButton is not null) NextSoundButton.IsEnabled = !locked;
        if (StopSoundButton is not null) StopSoundButton.IsEnabled = !locked;
        if (SoundLoopToggleButton is not null) SoundLoopToggleButton.IsEnabled = !locked;
        if (PlayPauseButton is not null) PlayPauseButton.IsEnabled = !locked;
        if (TimelineHost is not null) TimelineHost.IsHitTestVisible = !locked;
        if (SoundVirtualVolumeSlider is not null) SoundVirtualVolumeSlider.IsEnabled = !locked;
        if (SoundMonitorVolumeSlider is not null) SoundMonitorVolumeSlider.IsEnabled = !locked;
        if (SoundBoardSceneLockOverlay is not null) SoundBoardSceneLockOverlay.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        UpdateScenePlaybackInteractivity();
    }

    private bool BlockSoundBoardPlaybackIfSceneActive()
    {
        if (!IsSceneActive)
        {
            return false;
        }

        UpdateSoundBoardSceneLockState();
        AppendLog("SoundBoard playback is unavailable while a scene is active. Use scene buttons or disable scenes first.");
        return true;
    }

    private static string SceneLoopPlaybackKey(string sceneId, string buttonId) => $"scene:{sceneId}:loop:{buttonId}";

    private static string SceneButtonPlaybackKey(string sceneId, string buttonId) => $"scene:{sceneId}:button:{buttonId}";

    private static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
        {
            return "00:00";
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private double GetSoundDurationSeconds(SoundBoardSound? sound)
    {
        if (sound is null || string.IsNullOrWhiteSpace(sound.FilePath) || !File.Exists(sound.FilePath))
        {
            return 0;
        }

        if (_soundDurationSecondsCache.TryGetValue(sound.FilePath, out var cached))
        {
            return cached;
        }

        try
        {
            using var reader = new NAudio.Wave.AudioFileReader(sound.FilePath);
            var seconds = Math.Max(0, reader.TotalTime.TotalSeconds);
            _soundDurationSecondsCache[sound.FilePath] = seconds;
            return seconds;
        }
        catch
        {
            _soundDurationSecondsCache[sound.FilePath] = 0;
            return 0;
        }
    }

    private void SelectSceneById(string sceneId)
    {
        var scene = _scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene is not null && ScenesListView is not null)
        {
            ScenesListView.SelectedItem = scene;
            _selectedScene = scene;
            UpdateSceneDetails();
        }
    }

    private static SceneSoundButton CloneSceneSoundButton(SceneSoundButton source)
    {
        return new SceneSoundButton
        {
            Id = source.Id,
            SoundId = source.SoundId,
            LocalName = source.LocalName,
            SceneHotkey = source.SceneHotkey,
            VirtualMicVolume = source.VirtualMicVolume,
            HeadphonesVolume = source.HeadphonesVolume,
            IsLooped = source.IsLooped,
            SortOrder = source.SortOrder
        };
    }

    private sealed class SceneTimelineBinding
    {
        public required string PlaybackKey { get; init; }
        public required Slider Slider { get; init; }
        public required TextBlock CurrentText { get; init; }
        public required TextBlock TotalText { get; init; }
        public Button? PlayPauseButton { get; init; }
        public double DurationFallbackSeconds { get; init; }
        public bool IsDragging { get; set; }
    }

    private sealed class SceneSoundButtonContext
    {
        public required VoiSeScene Scene { get; init; }
        public required SceneSoundButton Button { get; init; }
        public SoundBoardSound? Sound { get; init; }
        public string SourceName => Sound?.DisplayName ?? "Missing SoundBoard sound";
        public string DisplayName => string.IsNullOrWhiteSpace(Button.LocalName) ? SourceName : Button.LocalName!.Trim();
    }

    private void RebuildSceneSoundButtons()
    {
        if (LoopedSceneSoundsPanel is null || SceneSoundsPanel is null)
        {
            return;
        }

        LoopedSceneSoundsPanel.Children.Clear();
        SceneSoundsPanel.Children.Clear();
        _sceneTimelineBindings.Clear();

        if (_selectedScene is null)
        {
            LoopedSceneSoundsPanel.Children.Add(CreateLoopedSceneSoundSlot(null, "No scene selected"));
            RefreshSceneLoopActionButtons();
            return;
        }

        var orderedButtons = _selectedScene.SoundButtons
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.LocalName)
            .ToList();

        var loopedButton = orderedButtons.FirstOrDefault(b => b.IsLooped);
        LoopedSceneSoundsPanel.Children.Add(CreateLoopedSceneSoundSlot(loopedButton, "No sound"));

        foreach (var sceneButton in orderedButtons.Where(b => !b.IsLooped))
        {
            SceneSoundsPanel.Children.Add(CreateSceneSoundButton(sceneButton));
        }

        SceneSoundsPanel.Children.Add(CreateSceneAddSoundButton());

        RefreshSceneLoopActionButtons();
    }

    private void RefreshSceneLoopActionButtons()
    {
        var hasScene = _selectedScene is not null;
        var playbackEnabled = IsSelectedSceneActiveForPlayback();
        var hasLoopedSound = GetSelectedSceneLoopedButton() is not null;
        if (SceneLoopPlayLoopButton is not null) SceneLoopPlayLoopButton.IsEnabled = hasLoopedSound && playbackEnabled;
        if (SceneLoopPlayOnceButton is not null)
        {
            SceneLoopPlayOnceButton.IsEnabled = hasLoopedSound && playbackEnabled;
            var loopedButton = GetSelectedSceneLoopedButton();
            var playbackKey = _selectedScene is not null && loopedButton is not null
                ? SceneLoopPlaybackKey(_selectedScene.Id, loopedButton.Id)
                : null;
            var status = string.IsNullOrWhiteSpace(playbackKey)
                ? SoundboardStatus.Empty
                : _engine?.GetSoundStatus(playbackKey) ?? SoundboardStatus.Empty;
            SceneLoopPlayOnceButton.Content = status.IsActive && !status.IsPaused ? "\uE769" : "\uE768";
        }
        if (SceneLoopRemoveButton is not null) SceneLoopRemoveButton.IsEnabled = hasLoopedSound;
        if (SceneLoopChooseButton is not null) SceneLoopChooseButton.IsEnabled = hasScene;
    }

    private SceneSoundButton? GetSelectedSceneLoopedButton()
    {
        return _selectedScene?.SoundButtons
            .OrderBy(b => b.SortOrder)
            .FirstOrDefault(b => b.IsLooped);
    }

    private Button CreateSceneButtonShell()
    {
        return new Button
        {
            Width = SceneSoundButtonWidth,
            Height = SceneSoundButtonHeight,
            MinWidth = 0,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private FrameworkElement CreateLoopedSceneSoundSlot(SceneSoundButton? sceneButton, string emptyText)
    {
        var displayName = emptyText;
        var opacity = 0.62;
        SoundBoardSound? sound = null;
        string? playbackKey = null;

        if (sceneButton is not null)
        {
            sound = PickSound(sceneButton.SoundId);
            // Looped sound has no scene-local alias. Keep the visible name synchronized with SoundBoard.
            displayName = sound?.DisplayName ?? "Missing SoundBoard sound";
            opacity = 1.0;
            if (_selectedScene is not null)
            {
                playbackKey = SceneLoopPlaybackKey(_selectedScene.Id, sceneButton.Id);
            }
        }

        var root = new Grid
        {
            Height = 88,
            MinHeight = 88,
            RowSpacing = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SceneLoopIconHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });

        var label = new Border
        {
            Height = SceneLoopIconHeight,
            MinHeight = SceneLoopIconHeight,
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(sceneButton is null
                ? Microsoft.UI.ColorHelper.FromArgb(0x12, 0xFF, 0xFF, 0xFF)
                : Microsoft.UI.ColorHelper.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = displayName,
                Opacity = opacity,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetRow(label, 0);
        root.Children.Add(label);

        if (playbackKey is not null)
        {
            var timeline = CreateSceneTimeline(playbackKey, GetSoundDurationSeconds(sound), includePlayPauseButton: false);
            Grid.SetRow(timeline, 1);
            root.Children.Add(timeline);
        }
        else
        {
            var placeholder = CreateSceneTimeline($"scene:none:loop:{Guid.NewGuid():N}", 0, includePlayPauseButton: false);
            placeholder.Opacity = 0.45;
            placeholder.IsHitTestVisible = false;
            Grid.SetRow(placeholder, 1);
            root.Children.Add(placeholder);
        }

        return root;
    }

    private FrameworkElement CreateSceneTimeline(string playbackKey, double durationFallbackSeconds, bool includePlayPauseButton)
    {
        var root = new Grid
        {
            ColumnSpacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (includePlayPauseButton)
        {
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        else
        {
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        Button? playPause = null;
        var timelineColumn = includePlayPauseButton ? 1 : 0;
        if (includePlayPauseButton)
        {
            playPause = new Button
            {
                Content = "\uE768",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Width = 30,
                Height = 28,
                MinWidth = 0,
                Padding = new Thickness(0),
                Tag = playbackKey,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(playPause, "Play / pause this scene sound");
            playPause.Click += OnSceneTimelinePlayPauseClick;
            Grid.SetColumn(playPause, 0);
            root.Children.Add(playPause);
        }

        var timelineGrid = new Grid
        {
            RowSpacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        timelineGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        timelineGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = Math.Max(1, durationFallbackSeconds),
            Value = 0,
            StepFrequency = 0.05,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, -3, 0, -3),
            IsEnabled = IsSelectedSceneActiveForPlayback()
        };
        Grid.SetRow(slider, 0);
        timelineGrid.Children.Add(slider);

        var labels = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var currentText = new TextBlock
        {
            Text = "00:00",
            FontSize = 10,
            Opacity = 0.72,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        labels.Children.Add(currentText);

        var totalText = new TextBlock
        {
            Text = FormatDuration(durationFallbackSeconds),
            FontSize = 10,
            Opacity = 0.72,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(totalText, 1);
        labels.Children.Add(totalText);

        Grid.SetRow(labels, 1);
        timelineGrid.Children.Add(labels);

        Grid.SetColumn(timelineGrid, timelineColumn);
        root.Children.Add(timelineGrid);

        var binding = new SceneTimelineBinding
        {
            PlaybackKey = playbackKey,
            Slider = slider,
            CurrentText = currentText,
            TotalText = totalText,
            PlayPauseButton = playPause,
            DurationFallbackSeconds = durationFallbackSeconds
        };
        _sceneTimelineBindings[playbackKey] = binding;

        slider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            binding.IsDragging = true;
            e.Handled = true;
        }), true);
        slider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            binding.IsDragging = false;
            _engine?.SeekSound(slider.Value, playbackKey);
            e.Handled = true;
        }), true);
        slider.ValueChanged += (_, args) =>
        {
            if (binding.IsDragging)
            {
                _engine?.SeekSound(args.NewValue, playbackKey);
                currentText.Text = FormatDuration(args.NewValue);
            }
        };

        return root;
    }

    private void UpdateSceneTimelines()
    {
        if (_sceneTimelineBindings.Count == 0)
        {
            return;
        }

        foreach (var binding in _sceneTimelineBindings.Values.ToList())
        {
            var status = _engine?.GetSoundStatus(binding.PlaybackKey) ?? SoundboardStatus.Empty;
            var duration = status.IsActive && status.DurationSeconds > 0
                ? status.DurationSeconds
                : binding.DurationFallbackSeconds;
            duration = Math.Max(0, duration);
            var sliderMaximum = Math.Max(1, duration);

            if (!binding.IsDragging)
            {
                binding.Slider.Maximum = sliderMaximum;
                binding.Slider.Value = status.IsActive ? Clamp(status.CurrentSeconds, 0, sliderMaximum) : 0;
            }

            binding.CurrentText.Text = FormatDuration(status.IsActive ? status.CurrentSeconds : 0);
            binding.TotalText.Text = FormatDuration(duration);
            binding.Slider.Opacity = status.IsActive ? 1.0 : 0.52;
            binding.Slider.IsEnabled = IsSelectedSceneActiveForPlayback();
            if (binding.PlayPauseButton is not null)
            {
                binding.PlayPauseButton.Content = status.IsActive && !status.IsPaused ? "\uE769" : "\uE768";
            }
        }

        RefreshSceneLoopActionButtons();
    }

    private void OnSceneTimelinePlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string playbackKey })
        {
            return;
        }

        if (!RequireSceneActiveForPlayback(_selectedScene, "control scene sound playback"))
        {
            return;
        }

        var status = _engine?.GetSoundStatus(playbackKey) ?? SoundboardStatus.Empty;
        if (status.IsActive)
        {
            _engine?.ToggleSoundPause(playbackKey);
            UpdateSceneTimelines();
            return;
        }

        var loopedButton = GetSelectedSceneLoopedButton();
        if (loopedButton is not null && _selectedScene is not null && playbackKey == SceneLoopPlaybackKey(_selectedScene.Id, loopedButton.Id))
        {
            PlaySceneLoopedSound(loop: true);
        }
    }

    private Button CreateSceneSoundButton(SceneSoundButton sceneButton)
    {
        var sound = PickSound(sceneButton.SoundId);
        var context = new SceneSoundButtonContext
        {
            Scene = _selectedScene!,
            Button = sceneButton,
            Sound = sound
        };

        var button = CreateSceneButtonShell();
        button.Tag = context;
        button.Content = CreateSceneSoundButtonContent(context);
        button.ContextFlyout = CreateSceneSoundButtonFlyout(context);
        button.Click += OnSceneSoundButtonClick;
        return button;
    }

    private FrameworkElement CreateSceneSoundButtonContent(SceneSoundButtonContext context)
    {
        var root = new Grid
        {
            RowSpacing = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid
        {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = context.DisplayName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        });

        var hotkeyParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Button.SceneHotkey))
        {
            hotkeyParts.Add($"Scene: {context.Button.SceneHotkey}");
        }

        if (!string.IsNullOrWhiteSpace(context.Sound?.Hotkey))
        {
            hotkeyParts.Add($"SB: {context.Sound!.Hotkey}");
        }

        var hotkeyText = new TextBlock
        {
            Text = hotkeyParts.Count == 0 ? string.Empty : string.Join("  ", hotkeyParts),
            FontSize = 10,
            Opacity = 0.68,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 116
        };
        Grid.SetColumn(hotkeyText, 1);
        header.Children.Add(hotkeyText);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var playbackKey = SceneButtonPlaybackKey(context.Scene.Id, context.Button.Id);
        var timeline = CreateSceneTimeline(playbackKey, GetSoundDurationSeconds(context.Sound), includePlayPauseButton: false);
        Grid.SetRow(timeline, 1);
        root.Children.Add(timeline);

        return root;
    }

    private Button CreateSceneAddSoundButton()
    {
        var button = CreateSceneButtonShell();
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Content = new TextBlock
        {
            Text = "+",
            FontSize = 38,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom
        };
        flyout.Content = CreateSceneAddSoundFlyoutContent(flyout);
        button.Flyout = flyout;
        return button;
    }

    private FrameworkElement CreateSceneAddSoundFlyoutContent(Flyout flyout)
    {
        var panel = new StackPanel
        {
            Width = 420,
            Spacing = 8,
            Padding = new Thickness(10)
        };

        var categoryCombo = new ComboBox
        {
            Header = "Category",
            DisplayMemberPath = "Name",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = _library.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList()
        };
        categoryCombo.SelectedIndex = categoryCombo.Items.Count > 0 ? 0 : -1;

        var searchBox = new TextBox
        {
            Header = "Search",
            PlaceholderText = "Search sound"
        };

        var soundList = new ListView
        {
            Height = 260,
            SelectionMode = ListViewSelectionMode.Single,
            DisplayMemberPath = "ListText"
        };

        void refreshList()
        {
            soundList.ItemsSource = FilterSoundsForScenePicker(categoryCombo.SelectedItem as SoundBoardCategory, searchBox.Text)
                .ToList();
        }

        categoryCombo.SelectionChanged += (_, _) => refreshList();
        searchBox.TextChanged += (_, _) => refreshList();
        soundList.SelectionChanged += (_, _) =>
        {
            if (soundList.SelectedItem is SoundBoardSound sound)
            {
                AddSceneSoundButton(sound);
                flyout.Hide();
            }
        };

        panel.Children.Add(categoryCombo);
        panel.Children.Add(searchBox);
        panel.Children.Add(soundList);
        refreshList();
        return panel;
    }

    private IEnumerable<SoundBoardSound> FilterSoundsForScenePicker(SoundBoardCategory? category, string? searchText)
    {
        var query = _library.Sounds.AsEnumerable();
        if (category is not null)
        {
            query = query.Where(s => s.CategoryId == category.Id);
        }

        var term = searchText?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(s =>
                s.DisplayName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                s.OriginalFileName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(s.Hotkey) && s.Hotkey.Contains(term, StringComparison.CurrentCultureIgnoreCase)));
        }

        return query.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase);
    }

    private void AddSceneSoundButton(SoundBoardSound sound)
    {
        if (_selectedScene is null)
        {
            AppendLog("Select a scene before adding sounds.");
            return;
        }

        var nextOrder = _selectedScene.SoundButtons.Count == 0
            ? 0
            : _selectedScene.SoundButtons.Max(b => b.SortOrder) + 1;
        _selectedScene.SoundButtons.Add(new SceneSoundButton
        {
            SoundId = sound.Id,
            LocalName = sound.DisplayName,
            VirtualMicVolume = 1.0,
            HeadphonesVolume = 1.0,
            IsLooped = false,
            SortOrder = nextOrder
        });

        SaveSelectedSceneEditorChange($"Scene sound added: {sound.DisplayName}");
    }

    private Flyout CreateSceneSoundButtonFlyout(SceneSoundButtonContext context)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom
        };

        var panel = new StackPanel
        {
            Width = 420,
            Spacing = 8,
            Padding = new Thickness(10)
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"SoundBoard: {context.SourceName}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(context.Button.SceneHotkey)
                ? "Scene hotkey: none"
                : $"Scene hotkey: {context.Button.SceneHotkey}",
            FontSize = 12,
            Opacity = 0.72,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        });

        var actions = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Button makeAction(string text, int row, int column, int columnSpan = 1)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
            if (columnSpan > 1)
            {
                Grid.SetColumnSpan(button, columnSpan);
            }

            actions.Children.Add(button);
            return button;
        }

        var rename = makeAction("Rename", 0, 0);
        rename.Click += async (_, _) =>
        {
            flyout.Hide();
            await RenameSceneSoundButtonAsync(context.Button);
        };

        var chooseAnother = makeAction("Choose", 0, 1);
        chooseAnother.Click += async (_, _) =>
        {
            flyout.Hide();
            await ChooseAnotherSceneSoundAsync(context.Button);
        };

        var hotkey = makeAction("Hotkey", 1, 0);
        hotkey.IsEnabled = context.Sound is not null;
        ToolTipService.SetToolTip(hotkey, string.IsNullOrWhiteSpace(context.Button.SceneHotkey) ? "Set scene hotkey" : $"Scene hotkey: {context.Button.SceneHotkey}");
        hotkey.Click += async (_, _) =>
        {
            flyout.Hide();
            await EditSceneSoundHotkeyAsync(context.Button);
        };

        var stop = makeAction("Stop", 1, 1);
        stop.Click += (_, _) =>
        {
            flyout.Hide();
            if (RequireSceneActiveForPlayback(context.Scene, "stop scene sounds"))
            {
                StopSceneSoundButton(context.Scene, context.Button);
            }
        };

        var delete = makeAction("Delete", 2, 0, 2);
        delete.Click += (_, _) =>
        {
            flyout.Hide();
            DeleteSceneSoundButton(context.Button);
        };

        panel.Children.Add(actions);

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 2, 0, 2)
        });

        panel.Children.Add(CreateSceneSoundButtonVolumeRow(context.Button, "Headphones", isHeadphones: true));
        panel.Children.Add(CreateSceneSoundButtonVolumeRow(context.Button, "Virtual Mic", isHeadphones: false));

        flyout.Content = panel;
        return flyout;
    }

    private FrameworkElement CreateSceneSoundButtonVolumeRow(SceneSoundButton button, string label, bool isHeadphones)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

        var title = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.78
        };
        Grid.SetColumn(title, 0);
        grid.Children.Add(title);

        var currentValue = isHeadphones ? button.HeadphonesVolume : button.VirtualMicVolume;
        var value = new TextBlock
        {
            Text = $"{(int)Math.Round(Clamp(currentValue, 0, 1.5) * 100)}%",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.78
        };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1.5,
            StepFrequency = 0.01,
            Value = Clamp(currentValue, 0, 1.5),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        slider.ValueChanged += (_, args) =>
        {
            var clamped = Clamp(args.NewValue, 0, 1.5);
            if (isHeadphones)
            {
                button.HeadphonesVolume = clamped;
            }
            else
            {
                button.VirtualMicVolume = clamped;
            }

            value.Text = $"{(int)Math.Round(clamped * 100)}%";
            SaveSceneSoundButtonVolumeChange(button);
        };
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);

        return grid;
    }

    private void SaveSceneSoundButtonVolumeChange(SceneSoundButton button)
    {
        if (_selectedScene is null || !_selectedScene.SoundButtons.Any(candidate => candidate.Id == button.Id))
        {
            return;
        }

        try
        {
            _selectedScene.UpdatedAtUtc = DateTime.UtcNow;
            _sceneStore.OverwriteScene(_selectedScene);
            _engine?.UpdateSoundVolumes(
                (float)button.VirtualMicVolume,
                (float)button.HeadphonesVolume,
                SceneButtonPlaybackKey(_selectedScene.Id, button.Id));
        }
        catch (Exception ex)
        {
            AppendLog($"Scene sound volume save error: {ex.Message}");
        }
    }

    private void StopSceneSoundButton(VoiSeScene scene, SceneSoundButton button)
    {
        var key = button.IsLooped
            ? SceneLoopPlaybackKey(scene.Id, button.Id)
            : SceneButtonPlaybackKey(scene.Id, button.Id);
        _engine?.StopSound(key);
        UpdateSceneTimelines();
        AppendLog($"Scene sound stopped: {button.LocalName ?? button.SoundId}");
    }

    private void OnSceneSoundButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SceneSoundButtonContext context })
        {
            return;
        }

        if (context.Sound is null)
        {
            AppendLog($"Scene sound button target is missing: {context.DisplayName}");
            return;
        }

        if (!RequireSceneActiveForPlayback(context.Scene, "play scene sounds"))
        {
            return;
        }

        ToggleSceneSoundButtonPlayback(context.Scene, context.Button, context.Sound);
    }

    private void ToggleSceneSoundButtonPlayback(VoiSeScene scene, SceneSoundButton button, SoundBoardSound sound)
    {
        var playbackKey = SceneButtonPlaybackKey(scene.Id, button.Id);
        var status = _engine?.GetSoundStatus(playbackKey) ?? SoundboardStatus.Empty;
        if (status.IsActive)
        {
            var paused = _engine?.ToggleSoundPause(playbackKey) ?? false;
            UpdateSceneTimelines();
            AppendLog(paused
                ? $"Scene sound paused: {button.LocalName ?? sound.DisplayName}"
                : $"Scene sound resumed: {button.LocalName ?? sound.DisplayName}");
            return;
        }

        PlaySceneSound(scene, button, sound, false, "Scene sound");
    }

    private async Task RenameSceneSoundButtonAsync(SceneSoundButton sceneButton)
    {
        if (_selectedScene is null)
        {
            return;
        }

        var sound = PickSound(sceneButton.SoundId);
        var currentName = string.IsNullOrWhiteSpace(sceneButton.LocalName)
            ? sound?.DisplayName ?? "Scene sound"
            : sceneButton.LocalName!;
        var name = await ShowTextDialogAsync("Rename scene button", "Button name", currentName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        sceneButton.LocalName = name.Trim();
        SaveSelectedSceneEditorChange($"Scene button renamed: {sceneButton.LocalName}");
    }

    private async Task ChooseAnotherSceneSoundAsync(SceneSoundButton sceneButton)
    {
        var sound = await ShowSceneSoundPickerDialogAsync("Choose another sound");
        if (sound is null)
        {
            return;
        }

        if (_selectedScene is not null)
        {
            StopSceneSoundButton(_selectedScene, sceneButton);
        }

        sceneButton.SoundId = sound.Id;
        sceneButton.LocalName = sound.DisplayName;
        SaveSelectedSceneEditorChange($"Scene button sound changed: {sound.DisplayName}");
    }

    private void DeleteSceneSoundButton(SceneSoundButton sceneButton)
    {
        if (_selectedScene is null)
        {
            return;
        }

        var soundName = PickSound(sceneButton.SoundId)?.DisplayName ?? sceneButton.LocalName ?? sceneButton.SoundId;
        StopSceneSoundButton(_selectedScene, sceneButton);
        _selectedScene.SoundButtons.RemoveAll(b => b.Id == sceneButton.Id);
        NormalizeSceneButtonSortOrder(_selectedScene);
        SaveSelectedSceneEditorChange($"Scene button deleted: {soundName}");
    }

    private async Task EditSceneSoundHotkeyAsync(SceneSoundButton sceneButton)
    {
        var sound = PickSound(sceneButton.SoundId);
        if (sound is null)
        {
            AppendLog("Cannot assign scene hotkey: source SoundBoard sound is missing.");
            return;
        }

        var hotkey = await CaptureHotkeyDialogAsync(
            "Scene sound hotkey",
            "This hotkey belongs only to the selected scene button. SoundBoard hotkeys remain unchanged. Conflict priority: Transport, Scene, SoundBoard.",
            sceneButton.SceneHotkey);
        if (hotkey is null)
        {
            return;
        }

        sceneButton.SceneHotkey = NormalizeOptionalHotkey(hotkey);
        SaveSelectedSceneEditorChange($"Scene hotkey updated: {sound.DisplayName} -> {sceneButton.SceneHotkey ?? "none"}");
    }

    private void SetSceneSoundLooped(SceneSoundButton sceneButton, bool looped)
    {
        if (_selectedScene is null)
        {
            return;
        }

        if (looped)
        {
            foreach (var button in _selectedScene.SoundButtons.Where(b => b.Id != sceneButton.Id))
            {
                button.IsLooped = false;
            }
        }

        sceneButton.IsLooped = looped;
        sceneButton.SortOrder = _selectedScene.SoundButtons
            .Where(b => b.Id != sceneButton.Id && b.IsLooped == looped)
            .Select(b => b.SortOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;
        NormalizeSceneButtonSortOrder(_selectedScene);
        SaveSelectedSceneEditorChange(looped ? "Scene button moved to looped sound." : "Scene button moved to normal sounds.");
    }


    private static void EnforceSingleLoopedSceneSound(VoiSeScene scene)
    {
        var firstLoop = scene.SoundButtons
            .Where(button => button.IsLooped)
            .OrderBy(button => button.SortOrder)
            .FirstOrDefault();

        if (firstLoop is null)
        {
            return;
        }

        foreach (var button in scene.SoundButtons)
        {
            button.IsLooped = button.Id == firstLoop.Id;
        }
    }

    private static void NormalizeSceneButtonSortOrder(VoiSeScene scene)
    {
        var order = 0;
        foreach (var button in scene.SoundButtons.OrderBy(b => b.IsLooped ? 0 : 1).ThenBy(b => b.SortOrder).ToList())
        {
            button.SortOrder = order++;
        }
    }

    private async Task<SoundBoardSound?> ShowSceneSoundPickerDialogAsync(string title)
    {
        var panel = new StackPanel
        {
            Spacing = 10,
            MinWidth = 420
        };

        var categoryCombo = new ComboBox
        {
            Header = "Category",
            DisplayMemberPath = "Name",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = _library.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList()
        };
        categoryCombo.SelectedIndex = categoryCombo.Items.Count > 0 ? 0 : -1;

        var searchBox = new TextBox
        {
            Header = "Search",
            PlaceholderText = "Search sound"
        };

        var soundList = new ListView
        {
            Height = 300,
            SelectionMode = ListViewSelectionMode.Single,
            DisplayMemberPath = "ListText"
        };

        ContentDialog? dialog = null;
        void refreshList()
        {
            soundList.ItemsSource = FilterSoundsForScenePicker(categoryCombo.SelectedItem as SoundBoardCategory, searchBox.Text)
                .ToList();
            if (dialog is not null)
            {
                dialog.IsPrimaryButtonEnabled = soundList.SelectedItem is SoundBoardSound;
            }
        }

        categoryCombo.SelectionChanged += (_, _) => refreshList();
        searchBox.TextChanged += (_, _) => refreshList();
        soundList.SelectionChanged += (_, _) =>
        {
            if (dialog is not null)
            {
                dialog.IsPrimaryButtonEnabled = soundList.SelectedItem is SoundBoardSound;
            }
        };

        panel.Children.Add(categoryCombo);
        panel.Children.Add(searchBox);
        panel.Children.Add(soundList);
        refreshList();

        dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "Choose",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? soundList.SelectedItem as SoundBoardSound : null;
    }

    private void SaveSelectedSceneEditorChange(string logMessage)
    {
        if (_selectedScene is null || _loadingSceneUi)
        {
            return;
        }

        try
        {
            EnforceSingleLoopedSceneSound(_selectedScene);
            NormalizeSceneButtonSortOrder(_selectedScene);
            _sceneStore.OverwriteScene(_selectedScene);
            RefreshSceneListBinding();
            RebuildSceneSoundButtons();
            AppendLog(logMessage);
        }
        catch (Exception ex)
        {
            AppendLog($"Scene save error: {ex.Message}");
        }
    }

    private void RefreshSceneListBinding()
    {
        if (ScenesListView is null)
        {
            return;
        }

        UpdateSceneActiveFlags();
        var selectedId = _selectedScene?.Id;
        ScenesListView.ItemsSource = null;
        ScenesListView.ItemsSource = _scenes;

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            SelectSceneById(selectedId);
        }
    }

    private void StopSceneOneShotSounds(VoiSeScene scene, string logMessage)
    {
        foreach (var button in scene.SoundButtons.Where(button => !button.IsLooped))
        {
            _engine?.StopSound(SceneButtonPlaybackKey(scene.Id, button.Id));
        }

        UpdateSceneTimelines();
        AppendLog(logMessage);
    }

    private void ToggleSceneOneShotSoundsPause(VoiSeScene scene)
    {
        var oneShotKeys = scene.SoundButtons
            .Where(button => !button.IsLooped)
            .Select(button => SceneButtonPlaybackKey(scene.Id, button.Id))
            .ToList();
        var activeStatuses = oneShotKeys
            .Select(key => new { Key = key, Status = _engine?.GetSoundStatus(key) ?? SoundboardStatus.Empty })
            .Where(item => item.Status.IsActive)
            .ToList();

        if (activeStatuses.Count == 0)
        {
            AppendLog("No active scene one-shot sounds to pause.");
            return;
        }

        var shouldPause = activeStatuses.Any(item => !item.Status.IsPaused);
        foreach (var item in activeStatuses)
        {
            if (shouldPause && !item.Status.IsPaused)
            {
                _engine?.ToggleSoundPause(item.Key);
            }
            else if (!shouldPause && item.Status.IsPaused)
            {
                _engine?.ToggleSoundPause(item.Key);
            }
        }

        UpdateSceneTimelines();
        AppendLog(shouldPause ? "Scene one-shot sounds paused." : "Scene one-shot sounds resumed.");
    }

    private void OnSceneVoicePresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSceneUi || _selectedScene is null)
        {
            return;
        }

        if (SceneVoicePresetComboBox.SelectedItem is not VoicePreset preset)
        {
            return;
        }

        _selectedScene.VoicePresetName = preset.Name;
        _selectedScene.VoiceSliders = preset.Sliders?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, double>();
        SaveSelectedSceneEditorChange($"Scene voice preset selected: {preset.Name}");
    }

    private void OnSceneLoopPlayLoopClick(object sender, RoutedEventArgs e)
    {
        if (!RequireSceneActiveForPlayback(_selectedScene, "play the looped sound"))
        {
            return;
        }

        PlaySceneLoopedSound(loop: true);
    }

    private void OnSceneLoopPlayOnceClick(object sender, RoutedEventArgs e)
    {
        var loopedButton = GetSelectedSceneLoopedButton();
        if (_selectedScene is null || loopedButton is null)
        {
            return;
        }

        if (!RequireSceneActiveForPlayback(_selectedScene, "pause or resume the looped sound"))
        {
            return;
        }

        var playbackKey = SceneLoopPlaybackKey(_selectedScene.Id, loopedButton.Id);
        var status = _engine?.GetSoundStatus(playbackKey) ?? SoundboardStatus.Empty;
        if (status.IsActive)
        {
            var paused = _engine?.ToggleSoundPause(playbackKey) ?? false;
            UpdateSceneTimelines();
            RefreshSceneLoopActionButtons();
            AppendLog(paused ? "Scene looped sound paused." : "Scene looped sound resumed.");
            return;
        }

        PlaySceneLoopedSound(loop: true);
        RefreshSceneLoopActionButtons();
    }

    private void OnSceneLoopRemoveClick(object sender, RoutedEventArgs e)
    {
        var loopedButton = GetSelectedSceneLoopedButton();
        if (_selectedScene is null || loopedButton is null)
        {
            return;
        }

        var soundName = PickSound(loopedButton.SoundId)?.DisplayName ?? loopedButton.LocalName ?? loopedButton.SoundId;
        StopSceneSoundButton(_selectedScene, loopedButton);
        _selectedScene.SoundButtons.RemoveAll(b => b.Id == loopedButton.Id);
        NormalizeSceneButtonSortOrder(_selectedScene);
        SaveSelectedSceneEditorChange($"Scene looped sound removed: {soundName}");
    }

    private async void OnSceneLoopChooseClick(object sender, RoutedEventArgs e)
    {
        if (_selectedScene is null)
        {
            return;
        }

        var sound = await ShowSceneSoundPickerDialogAsync("Choose looped sound");
        if (sound is null)
        {
            return;
        }

        var loopedButton = GetSelectedSceneLoopedButton();
        if (loopedButton is null)
        {
            var nextOrder = _selectedScene.SoundButtons.Count == 0
                ? 0
                : _selectedScene.SoundButtons.Max(b => b.SortOrder) + 1;
            _selectedScene.SoundButtons.Add(new SceneSoundButton
            {
                SoundId = sound.Id,
                LocalName = null,
                VirtualMicVolume = 1.0,
                HeadphonesVolume = 1.0,
                IsLooped = true,
                SortOrder = nextOrder
            });
        }
        else
        {
            StopSceneSoundButton(_selectedScene, loopedButton);
            loopedButton.SoundId = sound.Id;
            loopedButton.LocalName = null;
        }

        EnforceSingleLoopedSceneSound(_selectedScene);
        SaveSelectedSceneEditorChange($"Scene looped sound selected: {sound.DisplayName}");
    }

    private void PlaySceneLoopedSound(bool loop)
    {
        var loopedButton = GetSelectedSceneLoopedButton();
        if (loopedButton is null)
        {
            AppendLog("No looped sound selected for this scene.");
            return;
        }

        var sound = PickSound(loopedButton.SoundId);
        if (sound is null)
        {
            AppendLog($"Scene looped sound target is missing: {loopedButton.LocalName ?? loopedButton.SoundId}");
            return;
        }

        if (_selectedScene is not null)
        {
            if (!RequireSceneActiveForPlayback(_selectedScene, "play the looped sound"))
            {
                return;
            }

            PlaySceneSound(_selectedScene, loopedButton, sound, loop, "Scene looped sound");
            RefreshSceneLoopActionButtons();
        }
    }

    private void OnSceneVoicePresetClearClick(object sender, RoutedEventArgs e)
    {
        if (_selectedScene is null)
        {
            return;
        }

        _selectedScene.VoicePresetName = null;
        _selectedScene.VoiceSliders.Clear();
        RefreshSceneVoicePresetComboBox();
        SaveSelectedSceneEditorChange("Scene voice preset cleared.");
    }

    private void OnSceneVoicePresetCreateClick(object sender, RoutedEventArgs e)
    {
        MainTabView.SelectedIndex = 1;
        AppendLog("Open Voice Changer to create a new preset.");
    }

    private void OnSceneAutostartLoopsChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSceneUi || _selectedScene is null || SceneAutostartLoopsCheckBox is null)
        {
            return;
        }

        _selectedScene.AutoStartLoopedSounds = SceneAutostartLoopsCheckBox.IsChecked == true;
        SaveSelectedSceneEditorChange(_selectedScene.AutoStartLoopedSounds
            ? "Scene loop autostart enabled."
            : "Scene loop autostart disabled.");
    }

    private void LoadVoicePresetsIntoUi()
    {
        try
        {
            _voicePresets = _voicePresetStore.LoadPresets();
            RebuildVoicePresetButtons();
            RefreshSceneVoicePresetComboBox();
            AppendLog($"Voice presets loaded: {_voicePresets.Count}. Folder: {_voicePresetStore.PresetsDirectory}");
        }
        catch (Exception ex)
        {
            AppendLog($"Voice presets load error: {ex.Message}");
        }
    }

    private void RebuildVoicePresetButtons()
    {
        if (VoicePresetsPanel is null)
        {
            return;
        }

        VoicePresetsPanel.Children.Clear();
        foreach (var preset in _voicePresets)
        {
            VoicePresetsPanel.Children.Add(CreateVoicePresetTile(preset));
        }

        VoicePresetsPanel.Children.Add(CreateNewVoicePresetTile());
        VoicePresetsPanel.Children.Add(CreateVoicePresetToolsTile());
    }


    private static readonly VoicePresetIconChoice[] VoicePresetIconChoices =
    {
        new("Microphone", "\uE720", true),
        new("Speaker", "\uE767", true),
        new("Headphones", "\uE7F6", true),
        new("Music", "\uE8D6", true),
        new("Radio", "\uE789", true),
        new("Robot", "🤖", false),
        new("Alien", "👽", false),
        new("Monster", "👾", false),
        new("Ghost", "👻", false),
        new("Skull", "💀", false),
        new("Fire", "🔥", false),
        new("Snow", "❄️", false),
        new("Storm", "⚡", false),
        new("Star", "⭐", false),
        new("Moon", "🌙", false),
        new("Sun", "☀️", false),
        new("Cloud", "☁️", false),
        new("Rain", "🌧️", false),
        new("Rainbow", "🌈", false),
        new("Wind", "💨", false),
        new("Water", "💧", false),
        new("Wave", "🌊", false),
        new("Leaf", "🍃", false),
        new("Tree", "🌲", false),
        new("Mushroom", "🍄", false),
        new("Cat", "🐱", false),
        new("Dog", "🐶", false),
        new("Wolf", "🐺", false),
        new("Dragon", "🐉", false),
        new("Owl", "🦉", false),
        new("Frog", "🐸", false),
        new("Bat", "🦇", false),
        new("Snake", "🐍", false),
        new("Fox", "🦊", false),
        new("Bear", "🐻", false),
        new("Laugh", "😄", false),
        new("Cool", "😎", false),
        new("Sad", "😢", false),
        new("Angry", "😡", false),
        new("Demon", "😈", false),
        new("Clown", "🤡", false),
        new("Wizard", "🧙", false),
        new("Ninja", "🥷", false),
        new("Crown", "👑", false),
        new("Gem", "💎", false),
        new("Crystal Ball", "🔮", false),
        new("Dice", "🎲", false),
        new("Game", "🎮", false),
        new("Drama", "🎭", false),
        new("Piano", "🎹", false),
        new("Guitar", "🎸", false),
        new("Drum", "🥁", false),
        new("Saxophone", "🎷", false),
        new("Trumpet", "🎺", false),
        new("Bell", "🔔", false),
        new("Alarm", "⏰", false),
        new("Hourglass", "⏳", false),
        new("Rocket", "🚀", false),
        new("UFO", "🛸", false),
        new("Car", "🚗", false),
        new("Train", "🚂", false),
        new("Ship", "🚢", false),
        new("Plane", "✈️", false),
        new("Sword", "⚔️", false),
        new("Shield", "🛡️", false),
        new("Bomb", "💣", false),
        new("Magic", "✨", false),
        new("Sparkles", "💫", false),
        new("Heart", "❤️", false),
        new("Broken Heart", "💔", false),
        new("Mask", "😷", false),
        new("Microphone Emoji", "🎙️", false),
        new("Headphones Emoji", "🎧", false),
        new("Speaker Emoji", "🔊", false),
        new("Muted", "🔇", false)
    };

    private static string NormalizeVoicePresetIcon(string? icon)
    {
        return string.IsNullOrWhiteSpace(icon) ? DefaultVoicePresetIcon : icon.Trim();
    }

    private static bool IsMdl2VoicePresetIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return true;
        }

        return VoicePresetIconChoices.Any(choice => choice.UseMdl2 && string.Equals(choice.Icon, icon, StringComparison.Ordinal));
    }

    private TextBlock CreateVoicePresetIconTextBlock(string? icon, double fontSize)
    {
        var normalized = NormalizeVoicePresetIcon(icon);
        return new TextBlock
        {
            Text = normalized,
            FontFamily = new FontFamily(IsMdl2VoicePresetIcon(normalized) ? "Segoe MDL2 Assets" : "Segoe UI Emoji"),
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
    }

    private static void AttachIconPickerWheelRouting(UIElement source, ScrollViewer targetScrollViewer)
    {
        source.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, args) =>
        {
            var delta = args.GetCurrentPoint(targetScrollViewer).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            TryScrollViewer(targetScrollViewer, delta, 52.0);
            args.Handled = true;
        }), true);
    }

    private async Task<(string Name, string Icon)?> ShowVoicePresetNameAndIconDialogAsync(string title, string initialName, string? initialIcon)
    {
        var selectedIcon = NormalizeVoicePresetIcon(initialIcon);
        var nameBox = new TextBox
        {
            PlaceholderText = "Preset name",
            Text = initialName,
            MinWidth = 360
        };

        var iconGrid = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 52,
            ItemHeight = 52,
            MaximumRowsOrColumns = 8,
            MinWidth = 416
        };

        var buttons = new List<ToggleButton>();
        foreach (var choice in VoicePresetIconChoices)
        {
            var button = new ToggleButton
            {
                Width = 44,
                Height = 44,
                MinWidth = 0,
                Tag = choice.Icon,
                Content = CreateVoicePresetIconTextBlock(choice.Icon, choice.UseMdl2 ? 22 : 24),
                IsChecked = string.Equals(choice.Icon, selectedIcon, StringComparison.Ordinal)
            };
            ToolTipService.SetToolTip(button, choice.Name);
            button.Checked += (_, _) =>
            {
                selectedIcon = (string)button.Tag;
                foreach (var other in buttons.Where(other => !ReferenceEquals(other, button)))
                {
                    other.IsChecked = false;
                }
            };
            buttons.Add(button);
            iconGrid.Children.Add(button);
        }

        if (!buttons.Any(button => button.IsChecked == true) && buttons.Count > 0)
        {
            buttons[0].IsChecked = true;
            selectedIcon = (string)buttons[0].Tag;
        }

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Icon",
            Opacity = 0.78,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        var iconScrollViewer = new ScrollViewer
        {
            Content = iconGrid,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            ZoomMode = ZoomMode.Disabled
        };
        AttachIconPickerWheelRouting(iconScrollViewer, iconScrollViewer);
        foreach (var button in buttons)
        {
            AttachIconPickerWheelRouting(button, iconScrollViewer);
        }

        panel.Children.Add(iconScrollViewer);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        ContentDialogResult result;
        _suppressMainTabWheelRouting = true;
        _activeIconPickerScrollViewer = iconScrollViewer;
        _activeIconPickerWheelZoneElement = iconScrollViewer;
        _activeModalWheelZoneLeftExtensionRatio = 0.0;
        _activeModalWheelZoneRightExtensionRatio = ModalWheelZoneExpandRightRatio;
        _activeModalWheelZoneHorizontalShiftRatio = IconPickerWheelZoneShiftRightRatio;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            _activeIconPickerScrollViewer = null;
            _activeIconPickerWheelZoneElement = null;
            _activeModalWheelZoneLeftExtensionRatio = 0.0;
            _activeModalWheelZoneRightExtensionRatio = 0.0;
            _activeModalWheelZoneHorizontalShiftRatio = 0.0;
            _suppressMainTabWheelRouting = false;
        }

        return result == ContentDialogResult.Primary
            ? (nameBox.Text.Trim(), selectedIcon)
            : null;
    }

    private FrameworkElement CreateVoicePresetTile(VoicePreset preset)
    {
        var stack = new StackPanel
        {
            Width = 104,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Left,
            Tag = preset
        };

        var button = new Button
        {
            Width = 84,
            Height = 84,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Tag = preset,
            Content = CreateVoicePresetIconTextBlock(preset.Icon, 34)
        };
        button.Click += OnVoicePresetClick;
        button.ContextFlyout = CreateVoicePresetFlyout(preset);
        stack.ContextFlyout = CreateVoicePresetFlyout(preset);

        var label = new TextBlock
        {
            Text = preset.Name,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 42,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        stack.Children.Add(button);
        stack.Children.Add(label);
        return stack;
    }

    private MenuFlyout CreateVoicePresetFlyout(VoicePreset preset)
    {
        var flyout = new MenuFlyout();

        var selectItem = new MenuFlyoutItem { Text = "Select" };
        selectItem.Click += (_, _) => ApplyVoicePreset(preset);
        flyout.Items.Add(selectItem);

        var renameItem = new MenuFlyoutItem { Text = "Rename" };
        renameItem.Click += async (_, _) => await RenameVoicePresetAsync(preset);
        flyout.Items.Add(renameItem);

        var recreateItem = new MenuFlyoutItem { Text = "Recreate" };
        recreateItem.Click += (_, _) => RecreateVoicePreset(preset);
        flyout.Items.Add(recreateItem);

        var hotkeyItem = new MenuFlyoutItem { Text = "Choose hotkey" };
        hotkeyItem.Click += async (_, _) => await EditVoicePresetHotkeysAsync(preset);
        flyout.Items.Add(hotkeyItem);

        var copyJsonFileItem = new MenuFlyoutItem { Text = "Copy JSON file" };
        copyJsonFileItem.Click += async (_, _) => await CopyVoicePresetJsonFileToClipboardAsync(preset);
        flyout.Items.Add(copyJsonFileItem);

        var deleteItem = new MenuFlyoutItem { Text = "Delete" };
        deleteItem.Click += async (_, _) => await DeleteVoicePresetAsync(preset);
        flyout.Items.Add(deleteItem);

        return flyout;
    }

    private async Task CopyVoicePresetJsonFileToClipboardAsync(VoicePreset preset)
    {
        try
        {
            var path = preset.FilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                var refreshed = _voicePresetStore.LoadPresets()
                    .FirstOrDefault(p => string.Equals(p.Name, preset.Name, StringComparison.CurrentCultureIgnoreCase));
                path = refreshed?.FilePath;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AppendLog($"Voice preset JSON copy failed: file not found for {preset.Name}.");
                return;
            }

            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var dataPackage = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            dataPackage.SetStorageItems(new[] { storageFile });
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
            AppendLog($"Voice preset JSON file copied to clipboard: {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            AppendLog($"Voice preset JSON copy error: {ex.Message}");
        }
    }

    private async Task RenameVoicePresetAsync(VoicePreset preset)
    {
        var edited = await ShowVoicePresetNameAndIconDialogAsync("Rename voice preset", preset.Name, preset.Icon);
        if (edited is null)
        {
            return;
        }

        var (newName, newIcon) = edited.Value;
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        try
        {
            _voicePresetStore.RenamePreset(preset, newName.Trim());
            preset.Icon = NormalizeVoicePresetIcon(newIcon);
            _voicePresetStore.OverwritePreset(preset);
            LoadVoicePresetsIntoUi();
            AppendLog($"Voice preset renamed: {preset.Name}");
        }
        catch (Exception ex)
        {
            AppendLog($"Voice preset rename error: {ex.Message}");
        }
    }

    private void RecreateVoicePreset(VoicePreset preset)
    {
        try
        {
            var recreated = CaptureCurrentVoicePreset(preset.Name);
            recreated.Icon = NormalizeVoicePresetIcon(preset.Icon);
            recreated.PushToTalkHotkey = preset.PushToTalkHotkey;
            recreated.PresetHotkey = preset.PresetHotkey;
            recreated.FilePath = preset.FilePath;
            _voicePresetStore.OverwritePreset(recreated);
            LoadVoicePresetsIntoUi();
            AppendLog($"Voice preset recreated from current sliders: {recreated.Name}");
        }
        catch (Exception ex)
        {
            AppendLog($"Voice preset recreate error: {ex.Message}");
        }
    }

    private async Task EditVoicePresetHotkeysAsync(VoicePreset preset)
    {
        var panel = new StackPanel
        {
            Spacing = 12
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Click a hotkey button, then press a key or Ctrl/Alt/Shift combination. Esc cancels capture. Plain A-Z and < > { } are local-only; NumPad keys and Ctrl/Alt/Shift combinations remain global.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });
        panel.Children.Add(CreateHotkeyCaptureRow("Push to talk", preset.PushToTalkHotkey, out var pushToTalkButton));
        panel.Children.Add(CreateHotkeyCaptureRow("Preset select", preset.PresetHotkey, out var presetButton));

        var dialog = new ContentDialog
        {
            Title = $"Hotkeys: {preset.Name}",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        _capturingHotkey = true;
        try
        {
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }
        }
        finally
        {
            _capturingHotkey = false;
        }

        preset.PushToTalkHotkey = NormalizeOptionalHotkey(GetHotkeyButtonValue(pushToTalkButton));
        preset.PresetHotkey = NormalizeOptionalHotkey(GetHotkeyButtonValue(presetButton));
        _voicePresetStore.OverwritePreset(preset);
        LoadVoicePresetsIntoUi();
        AppendLog($"Voice preset hotkeys saved: {preset.Name}");
    }

    private async Task DeleteVoicePresetAsync(VoicePreset preset)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete voice preset",
            Content = $"Delete preset '{preset.Name}'? The JSON file will be removed from the presets folder.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            _voicePresetStore.DeletePreset(preset);
            LoadVoicePresetsIntoUi();
            AppendLog($"Voice preset deleted: {preset.Name}");
        }
        catch (Exception ex)
        {
            AppendLog($"Voice preset delete error: {ex.Message}");
        }
    }

    private FrameworkElement CreateNewVoicePresetTile()
    {
        var stack = new StackPanel
        {
            Width = 104,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var button = new Button
        {
            Width = 84,
            Height = 84,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Content = new TextBlock
            {
                Text = "+",
                FontSize = 42,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += OnNewVoicePresetClick;

        var label = new TextBlock
        {
            Text = "New preset",
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        stack.Children.Add(button);
        stack.Children.Add(label);
        return stack;
    }

    private FrameworkElement CreateVoicePresetToolsTile()
    {
        var stack = new StackPanel
        {
            Width = 104,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var grid = new Grid
        {
            Width = 84,
            Height = 84,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            RowSpacing = 4
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var importButton = new Button
        {
            Content = "Import",
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        importButton.Click += OnImportVoicePresetClick;
        Grid.SetRow(importButton, 0);

        var folderButton = new Button
        {
            Content = "Folder",
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        folderButton.Click += OnOpenVoicePresetsFolderClick;
        Grid.SetRow(folderButton, 1);

        grid.Children.Add(importButton);
        grid.Children.Add(folderButton);

        var label = new TextBlock
        {
            Text = "Preset tools",
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        stack.Children.Add(grid);
        stack.Children.Add(label);
        return stack;
    }

    private async void OnImportVoicePresetClick(object sender, RoutedEventArgs e)
    {
        var filePath = await PickVoicePresetFileAsync();
        if (filePath is null)
        {
            return;
        }

        try
        {
            var importedPath = _voicePresetStore.ImportPreset(filePath);
            LoadVoicePresetsIntoUi();
            AppendLog($"Voice preset imported: {System.IO.Path.GetFileName(importedPath)}");
        }
        catch (Exception ex)
        {
            AppendLog($"Voice preset import error: {ex.Message}");
        }
    }

    private async Task<string?> PickVoicePresetFileAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private void OnOpenVoicePresetsFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_voicePresetStore.PresetsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _voicePresetStore.PresetsDirectory,
                UseShellExecute = true
            });
            AppendLog($"Voice presets folder opened: {_voicePresetStore.PresetsDirectory}");
        }
        catch (Exception ex)
        {
            AppendLog($"Open voice presets folder error: {ex.Message}");
        }
    }

    private void OnVoicePresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: VoicePreset preset })
        {
            return;
        }

        ApplyVoicePreset(preset);
    }

    private async void OnNewVoicePresetClick(object sender, RoutedEventArgs e)
    {
        var edited = await ShowVoicePresetNameAndIconDialogAsync("New voice preset", "New Preset", DefaultVoicePresetIcon);
        if (edited is null)
        {
            return;
        }

        var (name, icon) = edited.Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var preset = CaptureCurrentVoicePreset(name.Trim());
            preset.Icon = NormalizeVoicePresetIcon(icon);
            var path = _voicePresetStore.SavePreset(preset);
            LoadVoicePresetsIntoUi();
            AppendLog($"Voice preset saved: {preset.Name} -> {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            AppendLog($"Voice preset save error: {ex.Message}");
        }
    }

    private VoicePreset CaptureCurrentVoicePreset(string name)
    {
        return new VoicePreset
        {
            Name = name,
            Icon = DefaultVoicePresetIcon,
            Sliders = new Dictionary<string, double>
            {
                ["VoiceGain"] = GetVoiceValue(VoiceGainSlider, VoiceGainValueBox),
                ["Gate"] = GetVoiceValue(GateThresholdSlider, GateThresholdValueBox),
                ["Compressor"] = GetVoiceValue(CompressorThresholdSlider, CompressorThresholdValueBox),
                ["Pitch"] = GetVoiceValue(PitchSlider, PitchValueBox),
                ["Formant"] = GetVoiceValue(FormantSlider, FormantValueBox),
                ["Bass"] = GetVoiceValue(BassSlider, BassValueBox),
                ["Treble"] = GetVoiceValue(TrebleSlider, TrebleValueBox),
                ["Distortion"] = GetVoiceValue(DistortionSlider, DistortionValueBox),
                ["Robot"] = GetVoiceValue(RobotSlider, RobotValueBox),
                ["Tremolo"] = GetVoiceValue(TremoloSlider, TremoloValueBox),
                ["Echo"] = GetVoiceValue(EchoSlider, EchoValueBox),
                ["Reverb"] = GetVoiceValue(ReverbSlider, ReverbValueBox),
                ["Radio"] = GetVoiceValue(RadioSlider, RadioValueBox),
                ["BitCrusher"] = GetVoiceValue(BitCrusherSlider, BitCrusherValueBox),
                ["Alien"] = GetVoiceValue(AlienSlider, AlienValueBox)
            }
        };
    }

    private void ApplyVoicePreset(VoicePreset preset)
    {
        _loadingVoicePreset = true;
        try
        {
            SetVoiceControlFromPreset(VoiceGainSlider, VoiceGainValueBox, preset, "VoiceGain");
            SetVoiceControlFromPreset(GateThresholdSlider, GateThresholdValueBox, preset, "Gate");
            SetVoiceControlFromPreset(CompressorThresholdSlider, CompressorThresholdValueBox, preset, "Compressor");
            SetVoiceControlFromPreset(PitchSlider, PitchValueBox, preset, "Pitch");
            SetVoiceControlFromPreset(FormantSlider, FormantValueBox, preset, "Formant");
            SetVoiceControlFromPreset(BassSlider, BassValueBox, preset, "Bass");
            SetVoiceControlFromPreset(TrebleSlider, TrebleValueBox, preset, "Treble");
            SetVoiceControlFromPreset(DistortionSlider, DistortionValueBox, preset, "Distortion");
            SetVoiceControlFromPreset(RobotSlider, RobotValueBox, preset, "Robot");
            SetVoiceControlFromPreset(TremoloSlider, TremoloValueBox, preset, "Tremolo");
            SetVoiceControlFromPreset(EchoSlider, EchoValueBox, preset, "Echo");
            SetVoiceControlFromPreset(ReverbSlider, ReverbValueBox, preset, "Reverb");
            SetVoiceControlFromPreset(RadioSlider, RadioValueBox, preset, "Radio");
            SetVoiceControlFromPreset(BitCrusherSlider, BitCrusherValueBox, preset, "BitCrusher");
            SetVoiceControlFromPreset(AlienSlider, AlienValueBox, preset, "Alien");
        }
        finally
        {
            _loadingVoicePreset = false;
        }

        UpdateVoiceSettingLabels();
        _lastAppliedVoicePresetName = preset.Name;
        ApplyLiveSettings($"voice preset applied: {preset.Name}");
    }

    private void SetVoiceControlFromPreset(Slider slider, TextBox textBox, VoicePreset preset, string key)
    {
        if (preset.Sliders is not null && preset.Sliders.TryGetValue(key, out var value))
        {
            SetVoiceControl(slider, textBox, value);
        }
    }

    private void SetVoiceControl(Slider slider, TextBox textBox, double value)
    {
        value = Clamp(value, VoiceValueMin, VoiceValueMax);
        _syncingVoiceControls = true;
        try
        {
            slider.Value = Clamp(value, -100, 100);
            textBox.Text = ((int)Math.Round(value)).ToString();
        }
        finally
        {
            _syncingVoiceControls = false;
        }
    }

    private void SyncVoiceTextBoxFromSlider(Slider slider)
    {
        var value = (int)Math.Round(slider.Value);
        var textBox = GetVoiceTextBoxForSlider(slider);
        if (textBox is not null)
        {
            textBox.Text = value.ToString();
        }
    }

    private void SyncVoiceSliderFromTextBox(TextBox textBox, double value)
    {
        var slider = GetVoiceSliderForTextBox(textBox);
        if (slider is not null)
        {
            slider.Value = Clamp(value, -100, 100);
        }
    }

    private TextBox? GetVoiceTextBoxForSlider(Slider slider)
    {
        if (slider == VoiceGainSlider) return VoiceGainValueBox;
        if (slider == GateThresholdSlider) return GateThresholdValueBox;
        if (slider == CompressorThresholdSlider) return CompressorThresholdValueBox;
        if (slider == PitchSlider) return PitchValueBox;
        if (slider == FormantSlider) return FormantValueBox;
        if (slider == BassSlider) return BassValueBox;
        if (slider == TrebleSlider) return TrebleValueBox;
        if (slider == DistortionSlider) return DistortionValueBox;
        if (slider == RobotSlider) return RobotValueBox;
        if (slider == TremoloSlider) return TremoloValueBox;
        if (slider == EchoSlider) return EchoValueBox;
        if (slider == ReverbSlider) return ReverbValueBox;
        if (slider == RadioSlider) return RadioValueBox;
        if (slider == BitCrusherSlider) return BitCrusherValueBox;
        if (slider == AlienSlider) return AlienValueBox;
        return null;
    }

    private Slider? GetVoiceSliderForTextBox(TextBox textBox)
    {
        if (textBox == VoiceGainValueBox) return VoiceGainSlider;
        if (textBox == GateThresholdValueBox) return GateThresholdSlider;
        if (textBox == CompressorThresholdValueBox) return CompressorThresholdSlider;
        if (textBox == PitchValueBox) return PitchSlider;
        if (textBox == FormantValueBox) return FormantSlider;
        if (textBox == BassValueBox) return BassSlider;
        if (textBox == TrebleValueBox) return TrebleSlider;
        if (textBox == DistortionValueBox) return DistortionSlider;
        if (textBox == RobotValueBox) return RobotSlider;
        if (textBox == TremoloValueBox) return TremoloSlider;
        if (textBox == EchoValueBox) return EchoSlider;
        if (textBox == ReverbValueBox) return ReverbSlider;
        if (textBox == RadioValueBox) return RadioSlider;
        if (textBox == BitCrusherValueBox) return BitCrusherSlider;
        if (textBox == AlienValueBox) return AlienSlider;
        return null;
    }

    private static bool TryParseVoiceValue(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-" || text.Trim() == "+")
        {
            return false;
        }

        if (!int.TryParse(text.Trim(), out var parsed))
        {
            return false;
        }

        value = Clamp(parsed, VoiceValueMin, VoiceValueMax);
        return true;
    }

    private static double GetVoiceValue(Slider slider, TextBox textBox)
    {
        return TryParseVoiceValue(textBox.Text, out var value)
            ? value
            : slider.Value;
    }

    private void UpdateVoiceMonitorButton()
    {
        var text = _voiceMonitorEnabled ? "Voice Monitor: On" : "Voice Monitor: Off";
        if (VoiceMonitorButton is not null)
        {
            VoiceMonitorButton.Content = text;
        }

        if (SceneVoiceMonitorButton is not null)
        {
            SceneVoiceMonitorButton.Content = text;
        }
    }

    private void UpdateAllLabels()
    {
        UpdateDelayLabel();
        UpdateSoundVolumeLabels();
        UpdateSceneVolumeLabels();
        UpdateOutputVolumeLabels();
        UpdateVoiceSettingLabels();
        UpdateVoiceMonitorButton();
        UpdateBottomStats();
        UpdateTimeline();
        UpdateBottomPanelVisibility();
        UpdateTransportHotkeySummary();
    }

    private void UpdateDelayLabel()
    {
        if (DelayLabel is null || SoundVirtualDelaySlider is null) return;
        DelayLabel.Text = $"SoundBoard Virtual Mic Delay: {(int)Math.Round(SoundVirtualDelaySlider.Value)} ms";
    }

    private void UpdateSoundVolumeLabels()
    {
        if (SoundVirtualVolumeLabel is null || SoundMonitorVolumeLabel is null) return;
        SoundVirtualVolumeLabel.Text = $"SoundBoard → Virtual Mic: {(int)Math.Round(SoundVirtualVolumeSlider.Value * 100)}%";
        SoundMonitorVolumeLabel.Text = $"SoundBoard → Headphones: {(int)Math.Round(SoundMonitorVolumeSlider.Value * 100)}%";
    }

    private void UpdateSceneVolumeLabels()
    {
        if (SceneLoopHeadphonesVolumeValueBox is not null && SceneLoopHeadphonesVolumeSlider is not null)
        {
            SceneLoopHeadphonesVolumeValueBox.Text = $"{(int)Math.Round(SceneLoopHeadphonesVolumeSlider.Value * 100)}%";
        }

        if (SceneLoopVirtualMicVolumeValueBox is not null && SceneLoopVirtualMicVolumeSlider is not null)
        {
            SceneLoopVirtualMicVolumeValueBox.Text = $"{(int)Math.Round(SceneLoopVirtualMicVolumeSlider.Value * 100)}%";
        }

    }

    private void UpdateOutputVolumeLabels()
    {
        if (VirtualOutputVolumeLabel is null || VirtualOutputVolumeSlider is null) return;
        VirtualOutputVolumeLabel.Text = $"Virtual Mic Master: {(int)Math.Round(VirtualOutputVolumeSlider.Value * 100)}%";
    }

    private void UpdateVoiceSettingLabels()
    {
        if (VoiceGainLabel is null) return;
        VoiceGainLabel.Text = "Voice Gain";
        GateThresholdLabel.Text = "Gate";
        CompressorThresholdLabel.Text = "Compressor";
        PitchLabel.Text = "Pitch";
        FormantLabel.Text = "Formant";
        BassLabel.Text = "Bass";
        TrebleLabel.Text = "Treble";
        DistortionLabel.Text = "Distortion";
        RobotLabel.Text = "Robot";
        TremoloLabel.Text = "Tremolo";
        EchoLabel.Text = "Echo";
        ReverbLabel.Text = "Reverb";
        RadioLabel.Text = "Radio";
        BitCrusherLabel.Text = "Bit Crusher";
        AlienLabel.Text = "Alien";
    }

    private static string FormatSigned(double value)
    {
        var rounded = (int)Math.Round(value);
        return rounded > 0 ? "+" + rounded : rounded.ToString();
    }

    private void UpdateBottomStats()
    {
        if (CategoryInfoTextBlock is null)
        {
            return;
        }

        var category = CurrentCategory;
        var soundsInCategory = category is null
            ? Enumerable.Empty<SoundBoardSound>()
            : _library.Sounds.Where(s => s.CategoryId == category.Id);
        var soundCount = soundsInCategory.Count();
        var categoryUsage = category?.UsageCount ?? 0;
        var soundUsage = soundsInCategory.Sum(s => s.UsageCount);
        var visibleCount = FilterSoundsForSelectedCategory().Count();

        CategoryInfoTextBlock.Text =
            $"Categories: {_library.Categories.Count}\n" +
            $"Tracks in category: {soundCount}\n" +
            $"Visible tracks: {visibleCount}\n" +
            $"Category uses: {categoryUsage}\n" +
            $"Track uses: {soundUsage}";
    }

    private void SaveCurrentSettings()
    {
        if (_loadingSettings) return;

        var input = InputDeviceComboBox?.SelectedItem as AudioDeviceInfo;
        var virtualOutput = VirtualOutputComboBox?.SelectedItem as AudioDeviceInfo;
        var monitor = MonitorOutputComboBox?.SelectedItem as AudioDeviceInfo;

        _settings.InputDeviceId = input?.Id;
        _settings.InputDeviceName = input?.FriendlyName;
        _settings.VirtualOutputDeviceId = virtualOutput?.Id;
        _settings.VirtualOutputDeviceName = virtualOutput?.FriendlyName;
        _settings.MonitorOutputDeviceId = monitor?.Id;
        _settings.MonitorOutputDeviceName = monitor?.FriendlyName;
        _settings.LastSoundFilePath = _soundFilePath;
        _settings.LastSoundId = _selectedSound?.Id;
        _settings.LastSoundCategoryId = CurrentCategory?.Id;

        _settings.VirtualMicMasterVolume = VirtualOutputVolumeSlider?.Value ?? 1.0;
        _settings.VoiceMonitorEnabled = _voiceMonitorEnabled;
        _settings.SoundBoardVirtualMicVolume = SoundVirtualVolumeSlider?.Value ?? 1.0;
        _settings.SoundBoardHeadphonesVolume = SoundMonitorVolumeSlider?.Value ?? 1.0;
        _settings.SoundBoardVirtualMicDelayMs = SoundVirtualDelaySlider?.Value ?? 85.0;
        _settings.VoiceGain = GetVoiceValue(VoiceGainSlider, VoiceGainValueBox);
        _settings.VoiceGate = GetVoiceValue(GateThresholdSlider, GateThresholdValueBox);
        _settings.VoiceCompressor = GetVoiceValue(CompressorThresholdSlider, CompressorThresholdValueBox);
        _settings.VoicePitch = GetVoiceValue(PitchSlider, PitchValueBox);
        _settings.VoiceFormant = GetVoiceValue(FormantSlider, FormantValueBox);
        _settings.VoiceBass = GetVoiceValue(BassSlider, BassValueBox);
        _settings.VoiceTreble = GetVoiceValue(TrebleSlider, TrebleValueBox);
        _settings.VoiceDistortion = GetVoiceValue(DistortionSlider, DistortionValueBox);
        _settings.VoiceRobot = GetVoiceValue(RobotSlider, RobotValueBox);
        _settings.VoiceTremolo = GetVoiceValue(TremoloSlider, TremoloValueBox);
        _settings.VoiceEcho = GetVoiceValue(EchoSlider, EchoValueBox);
        _settings.VoiceReverb = GetVoiceValue(ReverbSlider, ReverbValueBox);
        _settings.VoiceRadio = GetVoiceValue(RadioSlider, RadioValueBox);
        _settings.VoiceBitCrusher = GetVoiceValue(BitCrusherSlider, BitCrusherValueBox);
        _settings.VoiceAlien = GetVoiceValue(AlienSlider, AlienValueBox);

        // Keep legacy dB fields meaningful for older settings readers.
        _settings.VoiceGainDb = MapCentered(_settings.VoiceGain, 0, -24, 12);
        _settings.GateThresholdDb = MapCentered(_settings.VoiceGate, -45, -70, -20);
        _settings.CompressorThresholdDb = MapCentered(_settings.VoiceCompressor, -24, -60, -6);

        _settingsStore.Save(_settings);
    }



    private async Task ShowMessageDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (_logBuffer.Length > 0)
        {
            _logBuffer.AppendLine();
        }

        _logBuffer.Append(line);
    }
    private readonly record struct VoicePresetIconChoice(string Name, string Icon, bool UseMdl2);

    private sealed record ThemeComboItem(string DisplayName, string? FilePath, bool IsDefault);
}

