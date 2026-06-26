using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VoiSe.Audio;
using Windows.Storage.Pickers;
using Windows.Foundation;
using WinRT.Interop;

namespace VoiSe.App;

public sealed partial class MainWindow : Window
{
    private readonly AudioDeviceCatalog _catalog = new();
    private readonly DispatcherTimer _routeRestartTimer;
    private readonly DispatcherTimer _timelineTimer;
    private readonly SettingsStore _settingsStore = new();
    private readonly SoundBoardLibraryStore _libraryStore;
    private VoiSeUserSettings _settings;
    private SoundBoardLibrary _library;
    private Gate2UnifiedAudioEngine? _engine;
    private string? _soundFilePath;
    private SoundBoardSound? _selectedSound;
    private bool _loadingLibrary;
    private bool _refreshingDevices;
    private bool _manualStopRequested;
    private string _pendingRestartReason = "settings changed";
    private bool _voiceMonitorEnabled;
    private bool _loadingSettings = true;
    private bool _loadedOnce;
    private bool _timelineUserDragging;
    private double _timelineMaximumSeconds = 1.0;

    public MainWindow()
    {
        StartupLog.Write("MainWindow constructor started.");
        _settings = _settingsStore.Load();
        _libraryStore = new SoundBoardLibraryStore(_settingsStore.DataDirectory);
        _library = _libraryStore.Load();
        InitializeComponent();
        RegisterWheelRoutingHandlers();
        MainTabView.SelectionChanged += OnMainTabSelectionChanged;
        Closed += OnClosed;
        Activated += OnActivated;

        _routeRestartTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _routeRestartTimer.Tick += OnRouteRestartTimerTick;

        _timelineTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timelineTimer.Tick += OnTimelineTimerTick;
        _timelineTimer.Start();

        AppendLog("Gate 5.19 UI started.");
        AppendLog($"Settings path: {_settingsStore.SettingsPath}");
        StartupLog.Write("MainWindow initialized; waiting for first activation.");
    }

    private void RegisterWheelRoutingHandlers()
    {
        // Gate 5.19: no fullscreen coordinate hacks. The track list and Settings log
        // own their real layout area and receive wheel events directly.
        SoundListView.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnSoundListPointerWheelChanged), true);
        LogTextBox.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnLogPointerWheelChanged), true);
    }

    private void OnSoundListPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(SoundListView).Properties.MouseWheelDelta;
        if (delta != 0 && TryScrollElement(SoundListView, delta))
        {
            e.Handled = true;
        }
    }

    private void OnLogPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(LogTextBox).Properties.MouseWheelDelta;
        if (delta != 0 && TryScrollElement(LogTextBox, delta))
        {
            e.Handled = true;
        }
    }



    private bool TryScrollElement(DependencyObject? scrollOwner, int wheelDelta)
    {
        var scrollViewer = FindDescendant<ScrollViewer>(scrollOwner);
        if (scrollViewer is null)
        {
            return false;
        }

        // MouseWheelDelta is usually +120 for wheel up and -120 for wheel down.
        // ScrollViewer vertical offset grows downward, so subtract delta.
        var target = scrollViewer.VerticalOffset - wheelDelta;
        target = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, target));
        scrollViewer.ChangeView(null, target, null, disableAnimation: true);
        return true;
    }

    private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
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
            StartupLog.Write("Gate 5.19 restore started.");

            ApplyStoredScalarSettingsToControls();
            AppendLog("Saved scalar settings applied.");
            StartupLog.Write("Gate 5.19 scalar settings applied.");

            RefreshDevices(saveAfterRefresh: false);
            LoadSoundBoardLibraryIntoUi();
            AppendLog("Settings restored.");
            StartupLog.Write("Gate 5.19 restore completed.");
        }
        catch (Exception ex)
        {
            StartupLog.Write("Gate 5.19 restore error: " + ex);
            AppendLog($"Settings restore error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _loadingSettings = false;
            UpdateAllLabels();
        }
    }

    private void ApplyStoredScalarSettingsToControls()
    {
        _soundFilePath = string.IsNullOrWhiteSpace(_settings.LastSoundFilePath) ? null : _settings.LastSoundFilePath;

        VirtualOutputVolumeSlider.Value = Clamp(_settings.VirtualMicMasterVolume, 0, 1.5);
        SoundVirtualVolumeSlider.Value = Clamp(_settings.SoundBoardVirtualMicVolume, 0, 1.5);
        SoundMonitorVolumeSlider.Value = Clamp(_settings.SoundBoardHeadphonesVolume, 0, 1.5);
        SoundVirtualDelaySlider.Value = Clamp(_settings.SoundBoardVirtualMicDelayMs, 0, 300);
        VoiceGainSlider.Value = Clamp(_settings.VoiceGainDb, -24, 12);
        GateThresholdSlider.Value = Clamp(_settings.GateThresholdDb, -70, -20);
        CompressorThresholdSlider.Value = Clamp(_settings.CompressorThresholdDb, -40, 0);
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
        // Gate 5.6: bottom stats panel was removed. Settings tab owns the log panel now.
    }

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

        var selectedVirtual = PickById(renderDevices, oldVirtualId)
            ?? PickByExactName(renderDevices, _settings.VirtualOutputDeviceName)
            ?? PickByName(renderDevices, _settings.VirtualOutputDeviceName)
            ?? PickByName(renderDevices, "CABLE Input")
            ?? renderDevices.FirstOrDefault();

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
                SoundListView.SelectedItem = _selectedSound;
                _soundFilePath = _selectedSound.FilePath;
            }

            UpdateBottomStats();
            AppendLog($"SoundBoard library loaded: {_library.Categories.Count} categories, {_library.Sounds.Count} sounds.");
            AppendLog($"SoundBoard data: {_libraryStore.LibraryPath}");
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
        var sounds = FilterSoundsForSelectedCategory().ToList();
        SoundListView.ItemsSource = sounds;
        if (_selectedSound is not null)
        {
            SoundListView.SelectedItem = sounds.FirstOrDefault(s => s.Id == _selectedSound.Id);
        }
        UpdateBottomStats();
    }

    private IEnumerable<SoundBoardSound> FilterSoundsForSelectedCategory()
    {
        var category = CategoryComboBox.SelectedItem as SoundBoardCategory;
        var query = _library.Sounds.AsEnumerable();
        if (category is not null)
        {
            query = query.Where(s => s.CategoryId == category.Id);
        }
        return query.OrderBy(s => s.DisplayName);
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
        if (_loadingLibrary) return;

        _selectedSound = SoundListView.SelectedItem as SoundBoardSound;
        _soundFilePath = _selectedSound?.FilePath;
        SaveCurrentSettings();
        UpdateBottomStats();
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
            SoundListView.SelectedItem = sound;
            SaveCurrentSettings();
            AppendLog($"Track added to {category.Name}: {sound.DisplayName}");
        }
        catch (Exception ex)
        {
            AppendLog($"Add track error: {ex.Message}");
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

    private void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        var category = CurrentCategory;
        if (category is null)
        {
            AppendLog("Select a category to delete.");
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
        if (FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is { } item && item.Content is SoundBoardSound sound)
        {
            SoundListView.SelectedItem = sound;
            _selectedSound = sound;
            _soundFilePath = sound.FilePath;
        }
    }

    private void OnSoundListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is { } item && item.Content is SoundBoardSound sound)
        {
            SoundListView.SelectedItem = sound;
            _selectedSound = sound;
            _soundFilePath = sound.FilePath;
            SaveCurrentSettings();
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
        var hotkey = await ShowTextDialogAsync("Assign hotkey", "Example: Ctrl+Alt+1", _selectedSound.Hotkey ?? string.Empty);
        if (hotkey is null) return;
        _libraryStore.SetHotkey(_library, _selectedSound, hotkey);
        RefreshSoundList();
        SaveCurrentSettings();
        AppendLog($"Hotkey assigned: {_selectedSound.DisplayName} -> {hotkey}");
    }

    private async void OnSoundContextRenameClick(object sender, RoutedEventArgs e)
    {
        if (_selectedSound is null) return;
        var name = await ShowTextDialogAsync("Rename track", "Display name", _selectedSound.DisplayName);
        if (name is null) return;
        _libraryStore.RenameSound(_library, _selectedSound, name);
        RefreshSoundList();
        SaveCurrentSettings();
        AppendLog($"Track renamed: {name}");
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
            EngineStatusTextBlock.Text = "Running";
            AppendLog($"Engine started. Input: {input.FriendlyName}");
            AppendLog($"Virtual output: {virtualOutput.FriendlyName}");
            AppendLog($"Monitor: {(monitor is null ? "disabled" : monitor.FriendlyName)}");
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
        }
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
        return new EffectSettings
        {
            VoiceGainDb = (float)VoiceGainSlider.Value,
            GateThresholdDb = (float)GateThresholdSlider.Value,
            CompressorThresholdDb = (float)CompressorThresholdSlider.Value,
            GateEnabled = true,
            CompressorEnabled = true,
            LimiterEnabled = true,
            LimiterCeilingDb = -1.0f,
            VirtualOutputGain = (float)VirtualOutputVolumeSlider.Value,
            VoiceMonitorGain = _voiceMonitorEnabled ? 1.0f : 0.0f
        };
    }

    private void OnPlaySoundClick(object sender, RoutedEventArgs e) => PlaySelectedSound();

    private void OnPlayPauseSoundClick(object sender, RoutedEventArgs e)
    {
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

    private void PlaySelectedSound()
    {
        if (_engine is null)
        {
            AppendLog("Start engine before playing sound.");
            return;
        }

        var soundPath = _selectedSound?.FilePath ?? _soundFilePath;
        if (string.IsNullOrWhiteSpace(soundPath) || !File.Exists(soundPath))
        {
            AppendLog("Choose an existing track from the SoundBoard library first.");
            return;
        }

        try
        {
            SaveCurrentSettings();
            var delayMs = (int)Math.Round(SoundVirtualDelaySlider.Value);
            var virtualVolume = (float)SoundVirtualVolumeSlider.Value;
            var monitorVolume = (float)SoundMonitorVolumeSlider.Value;
            _engine.PlaySound(soundPath, virtualVolume, monitorVolume, delayMs);
            if (_selectedSound is not null)
            {
                _libraryStore.IncrementUsage(_library, _selectedSound);
            }
            UpdateBottomStats();
            AppendLog($"Sound started: {_selectedSound?.DisplayName ?? System.IO.Path.GetFileName(soundPath)}. Virtual delay: {delayMs} ms.");
        }
        catch (Exception ex)
        {
            AppendLog($"Sound playback error: {ex.Message}");
        }
    }

    private void OnStopSoundClick(object sender, RoutedEventArgs e)
    {
        _engine?.StopSound();
        UpdateTimeline();
        AppendLog("Sound stopped.");
    }


    private void OnPreviousSoundClick(object sender, RoutedEventArgs e)
    {
        SelectRelativeSound(-1, play: true);
    }

    private void OnNextSoundClick(object sender, RoutedEventArgs e)
    {
        SelectRelativeSound(1, play: true);
    }

    private void SelectRelativeSound(int delta, bool play)
    {
        var sounds = FilterSoundsForSelectedCategory().ToList();
        if (sounds.Count == 0) return;

        var index = _selectedSound is null ? -1 : sounds.FindIndex(s => s.Id == _selectedSound.Id);
        var nextIndex = index < 0 ? 0 : (index + delta + sounds.Count) % sounds.Count;
        _selectedSound = sounds[nextIndex];
        _soundFilePath = _selectedSound.FilePath;
        SoundListView.SelectedItem = _selectedSound;
        SaveCurrentSettings();
        if (play) PlaySelectedSound();
    }

    private void OnRouteSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingDevices || _loadingSettings) return;
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
        if (_engine is not null)
        {
            _engine.UpdateSoundVolumes((float)SoundVirtualVolumeSlider.Value, (float)SoundMonitorVolumeSlider.Value);
        }
    }

    private void OnOutputVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateOutputVolumeLabels();
        if (!_loadingSettings) ApplyLiveSettings("master output volume changed");
    }

    private void OnVoiceSettingsChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateVoiceSettingLabels();
        if (!_loadingSettings) ApplyLiveSettings("voice setting changed");
    }

    private void OnToggleVoiceMonitorClick(object sender, RoutedEventArgs e)
    {
        _voiceMonitorEnabled = !_voiceMonitorEnabled;
        UpdateVoiceMonitorButton();
        ApplyLiveSettings(_voiceMonitorEnabled ? "voice monitor enabled" : "voice monitor disabled");
    }

    private void OnTimelineHostPointerPressed(object sender, PointerRoutedEventArgs e)
    {
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

    private void OnTimelineTimerTick(object? sender, object e) => UpdateTimeline();

    private void UpdateTimeline()
    {
        if (TimelineHost is null)
        {
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
        TransportStatusTextBlock.Text = status.IsActive
            ? (status.IsPaused ? "Paused" : "Playing")
            : "No sound";
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

    private void UpdateVoiceMonitorButton()
    {
        if (VoiceMonitorButton is null) return;
        VoiceMonitorButton.Content = _voiceMonitorEnabled ? "Voice Monitor: On" : "Voice Monitor: Off";
    }

    private void UpdateAllLabels()
    {
        UpdateDelayLabel();
        UpdateSoundVolumeLabels();
        UpdateOutputVolumeLabels();
        UpdateVoiceSettingLabels();
        UpdateVoiceMonitorButton();
        UpdateBottomStats();
        UpdateTimeline();
        UpdateBottomPanelVisibility();
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

    private void UpdateOutputVolumeLabels()
    {
        if (VirtualOutputVolumeLabel is null || VirtualOutputVolumeSlider is null) return;
        VirtualOutputVolumeLabel.Text = $"Virtual Mic Master: {(int)Math.Round(VirtualOutputVolumeSlider.Value * 100)}%";
    }

    private void UpdateVoiceSettingLabels()
    {
        if (VoiceGainLabel is null || GateThresholdLabel is null || CompressorThresholdLabel is null) return;
        VoiceGainLabel.Text = $"Voice Gain: {(int)Math.Round(VoiceGainSlider.Value)} dB";
        GateThresholdLabel.Text = $"Gate Threshold: {(int)Math.Round(GateThresholdSlider.Value)} dB";
        CompressorThresholdLabel.Text = $"Compressor Threshold: {(int)Math.Round(CompressorThresholdSlider.Value)} dB";
    }

    private void UpdateBottomStats()
    {
        // Gate 5.5: category/sound stats footer was removed from the UI.
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
        _settings.VoiceGainDb = VoiceGainSlider?.Value ?? 0.0;
        _settings.GateThresholdDb = GateThresholdSlider?.Value ?? -45.0;
        _settings.CompressorThresholdDb = CompressorThresholdSlider?.Value ?? -18.0;

        _settingsStore.Save(_settings);
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogTextBox.Text = string.IsNullOrEmpty(LogTextBox.Text)
            ? line
            : LogTextBox.Text + Environment.NewLine + line;

        DispatcherQueue.TryEnqueue(() =>
        {
            LogTextBox.Select(LogTextBox.Text.Length, 0);
        });
    }
}
