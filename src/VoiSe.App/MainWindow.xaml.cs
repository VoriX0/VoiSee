using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VoiSe.Audio;
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
    private VoiSeUserSettings _settings;
    private SoundBoardLibrary _library;
    private IReadOnlyList<VoicePreset> _voicePresets = Array.Empty<VoicePreset>();
    private bool _loadingVoicePreset;
    private bool _syncingVoiceControls;
    private readonly DispatcherTimer _voiceSettingsApplyTimer;
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
    private string _trackSearchText = string.Empty;
    private List<SoundBoardSound> _visibleSounds = new();
    private string? _lastSoundRowClickSoundId;
    private DateTime _lastSoundRowClickUtc = DateTime.MinValue;
    private LowLevelMouseProc? _lowLevelMouseProc;
    private IntPtr _mouseHookHandle;
    private IntPtr _windowHandle;
    private const int WhMouseLl = 14;
    private const int WmMouseWheel = 0x020A;
    private const double SoundWheelZoneExpandUpRatio = 0.25;
    private const double SoundWheelZoneExpandRightRatio = 0.60;
    private const double SoundWheelZoneExpandBottomRatio = 0.40;
    private const double VoiceValueMin = -9999.0;
    private const double VoiceValueMax = 9999.0;

    public MainWindow()
    {
        StartupLog.Write("MainWindow constructor started.");
        _settings = _settingsStore.Load();
        _libraryStore = new SoundBoardLibraryStore(_settingsStore.DataDirectory);
        _voicePresetStore = new VoicePresetStore(_settingsStore.DataDirectory);
        _library = _libraryStore.Load();
        InitializeComponent();
        _windowHandle = WindowNative.GetWindowHandle(this);
        MainTabView.SelectionChanged += OnMainTabSelectionChanged;
        SoundInputOverlay.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnSoundInputOverlayPointerWheelChanged), true);
        InstallSoundBoardWheelHook();
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

        AppendLog("Gate 6.1 UI started.");
        AppendLog($"Settings path: {_settingsStore.SettingsPath}");
        StartupLog.Write("MainWindow initialized; waiting for first activation.");
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
            StartupLog.Write("Gate 6.1 restore started.");

            ApplyStoredScalarSettingsToControls();
            AppendLog("Saved scalar settings applied.");
            StartupLog.Write("Gate 6.1 scalar settings applied.");

            RefreshDevices(saveAfterRefresh: false);
            LoadSoundBoardLibraryIntoUi();
            LoadVoicePresetsIntoUi();
            AppendLog("Settings restored.");
            StartupLog.Write("Gate 6.1 restore completed.");
        }
        catch (Exception ex)
        {
            StartupLog.Write("Gate 6.1 restore error: " + ex);
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
        SetVoiceControl(InputGainSlider, InputGainValueBox, _settings.VoiceInputGain);
        SetVoiceControl(VoiceGainSlider, VoiceGainValueBox, _settings.VoiceGain);
        SetVoiceControl(PitchSlider, PitchValueBox, _settings.VoicePitch);
        SetVoiceControl(FormantSlider, FormantValueBox, _settings.VoiceFormant);
        SetVoiceControl(GateThresholdSlider, GateThresholdValueBox, _settings.VoiceGate);
        SetVoiceControl(CompressorThresholdSlider, CompressorThresholdValueBox, _settings.VoiceCompressor);
        SetVoiceControl(CompressionRatioSlider, CompressionRatioValueBox, _settings.VoiceCompressionRatio);
        SetVoiceControl(LimiterSlider, LimiterValueBox, _settings.VoiceLimiter);
        SetVoiceControl(RobotSlider, RobotValueBox, _settings.VoiceRobot);
        SetVoiceControl(RadioSlider, RadioValueBox, _settings.VoiceRadio);
        SetVoiceControl(ReverbSlider, ReverbValueBox, _settings.VoiceReverb);
        SetVoiceControl(BrightnessSlider, BrightnessValueBox, _settings.VoiceBrightness);
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

    private IntPtr LowLevelMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WmMouseWheel)
        {
            try
            {
                if (TryHandleFullTabSoundWheel(lParam))
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

    private bool TryHandleFullTabSoundWheel(IntPtr lParam)
    {
        if (MainTabView?.SelectedIndex != 0 || _windowHandleIsUnavailable())
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

        var scale = RootGrid?.XamlRoot?.RasterizationScale ?? 1.0;
        var xDip = clientPoint.X / scale;
        var yDip = clientPoint.Y / scale;

        if (RootGrid is null)
        {
            return false;
        }

        // Gate 5.34: keep the visual layout unchanged, but make the global
        // wheel catch-zone deliberately larger. The previous calibration proved
        // that the physical wheel hit area is offset from the visual Sounds list.
        // Instead of shifting the zone, expand it upward and to the right so the
        // whole visual list is covered even when WinUI/fullscreen coordinates drift.
        var tabTop = SoundBoardTabRoot.TransformToVisual(RootGrid)
            .TransformPoint(new Windows.Foundation.Point(0, 0))
            .Y;
        var usableHeight = Math.Max(1.0, RootGrid.ActualHeight - tabTop);

        var zoneLeft = 0.0;
        var zoneTop = Math.Max(tabTop, tabTop - usableHeight * SoundWheelZoneExpandUpRatio);
        var zoneRight = RootGrid.ActualWidth * (1.0 + SoundWheelZoneExpandRightRatio);
        var zoneBottom = RootGrid.ActualHeight + usableHeight * SoundWheelZoneExpandBottomRatio;

        if (xDip < zoneLeft || xDip > zoneRight || yDip < zoneTop || yDip > zoneBottom)
        {
            return false;
        }

        var delta = unchecked((short)((hookData.MouseData >> 16) & 0xffff));
        return delta != 0 && TryScrollSoundOverlay(delta);
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
        if (SoundOverlayScrollViewer is null || wheelDelta == 0)
        {
            return false;
        }

        // Smaller step than the native 120px-style jumps: about one row per wheel notch.
        var notches = Math.Max(1.0, Math.Abs(wheelDelta) / 120.0);
        var step = 14.0 * notches;
        var target = SoundOverlayScrollViewer.VerticalOffset - Math.Sign(wheelDelta) * step;
        target = Math.Max(0, Math.Min(SoundOverlayScrollViewer.ScrollableHeight, target));
        SoundOverlayScrollViewer.ChangeView(null, target, null, disableAnimation: false);
        return true;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

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
        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
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
                _soundFilePath = _selectedSound.FilePath;
                RefreshSoundList();
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
        var replace = new MenuFlyoutItem { Text = "Choose Another File" };
        replace.Click += OnSoundContextReplaceFileClick;
        var delete = new MenuFlyoutItem { Text = "Delete From Category" };
        delete.Click += OnSoundContextDeleteClick;

        flyout.Items.Add(play);
        flyout.Items.Add(hotkey);
        flyout.Items.Add(rename);
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
            InputGainDb = (float)MapCentered(GetVoiceValue(InputGainSlider, InputGainValueBox), 0, -12, 12),
            VoiceGainDb = (float)MapCentered(GetVoiceValue(VoiceGainSlider, VoiceGainValueBox), 0, -24, 12),
            GateThresholdDb = (float)MapCentered(GetVoiceValue(GateThresholdSlider, GateThresholdValueBox), -45, -70, -20),
            CompressorThresholdDb = (float)MapCentered(GetVoiceValue(CompressorThresholdSlider, CompressorThresholdValueBox), -18, -40, 0),
            CompressorRatio = (float)MapCentered(GetVoiceValue(CompressionRatioSlider, CompressionRatioValueBox), 3, 1, 8),
            GateEnabled = true,
            CompressorEnabled = true,
            LimiterEnabled = true,
            LimiterCeilingDb = (float)MapCentered(GetVoiceValue(LimiterSlider, LimiterValueBox), -1, -12, -0.1),
            VirtualOutputGain = (float)VirtualOutputVolumeSlider.Value,
            VoiceMonitorGain = _voiceMonitorEnabled ? 1.0f : 0.0f
        };
    }

    private static double MapCentered(double normalized, double center, double min, double max)
    {
        normalized = Clamp(normalized, -100, 100);
        return normalized < 0
            ? center + (normalized / 100.0) * (center - min)
            : center + (normalized / 100.0) * (max - center);
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
        SelectSound(sounds[nextIndex]);
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

    private void LoadVoicePresetsIntoUi()
    {
        try
        {
            _voicePresets = _voicePresetStore.LoadPresets();
            RebuildVoicePresetButtons();
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
            Content = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(preset.Icon) ? "🎙️" : preset.Icon,
                FontSize = 30,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
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

        var deleteItem = new MenuFlyoutItem { Text = "Delete" };
        deleteItem.Click += async (_, _) => await DeleteVoicePresetAsync(preset);
        flyout.Items.Add(deleteItem);

        return flyout;
    }

    private async Task RenameVoicePresetAsync(VoicePreset preset)
    {
        var newName = await ShowTextDialogAsync("Rename voice preset", "Preset name", preset.Name);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        try
        {
            _voicePresetStore.RenamePreset(preset, newName.Trim());
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
            recreated.Icon = string.IsNullOrWhiteSpace(preset.Icon) ? "🎙️" : preset.Icon;
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
        var pushToTalkBox = new TextBox
        {
            Header = "Push to talk",
            PlaceholderText = "Hold hotkey to use this voice",
            Text = preset.PushToTalkHotkey ?? string.Empty,
            MinWidth = 360
        };

        var presetBox = new TextBox
        {
            Header = "Preset select",
            PlaceholderText = "Press once to apply this preset",
            Text = preset.PresetHotkey ?? string.Empty,
            MinWidth = 360
        };

        var panel = new StackPanel
        {
            Spacing = 12
        };
        panel.Children.Add(pushToTalkBox);
        panel.Children.Add(presetBox);

        var dialog = new ContentDialog
        {
            Title = $"Hotkeys: {preset.Name}",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        preset.PushToTalkHotkey = string.IsNullOrWhiteSpace(pushToTalkBox.Text) ? null : pushToTalkBox.Text.Trim();
        preset.PresetHotkey = string.IsNullOrWhiteSpace(presetBox.Text) ? null : presetBox.Text.Trim();
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
        var name = await ShowTextDialogAsync("New voice preset", "Preset name", "New Preset");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var preset = CaptureCurrentVoicePreset(name.Trim());
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
            Icon = "🎙️",
            Sliders = new Dictionary<string, double>
            {
                ["InputGain"] = GetVoiceValue(InputGainSlider, InputGainValueBox),
                ["VoiceGain"] = GetVoiceValue(VoiceGainSlider, VoiceGainValueBox),
                ["Pitch"] = GetVoiceValue(PitchSlider, PitchValueBox),
                ["Formant"] = GetVoiceValue(FormantSlider, FormantValueBox),
                ["Gate"] = GetVoiceValue(GateThresholdSlider, GateThresholdValueBox),
                ["Compressor"] = GetVoiceValue(CompressorThresholdSlider, CompressorThresholdValueBox),
                ["CompressionRatio"] = GetVoiceValue(CompressionRatioSlider, CompressionRatioValueBox),
                ["Limiter"] = GetVoiceValue(LimiterSlider, LimiterValueBox),
                ["Robot"] = GetVoiceValue(RobotSlider, RobotValueBox),
                ["Radio"] = GetVoiceValue(RadioSlider, RadioValueBox),
                ["Reverb"] = GetVoiceValue(ReverbSlider, ReverbValueBox),
                ["Brightness"] = GetVoiceValue(BrightnessSlider, BrightnessValueBox)
            }
        };
    }

    private void ApplyVoicePreset(VoicePreset preset)
    {
        _loadingVoicePreset = true;
        try
        {
            SetVoiceControlFromPreset(InputGainSlider, InputGainValueBox, preset, "InputGain");
            SetVoiceControlFromPreset(VoiceGainSlider, VoiceGainValueBox, preset, "VoiceGain");
            SetVoiceControlFromPreset(PitchSlider, PitchValueBox, preset, "Pitch");
            SetVoiceControlFromPreset(FormantSlider, FormantValueBox, preset, "Formant");
            SetVoiceControlFromPreset(GateThresholdSlider, GateThresholdValueBox, preset, "Gate");
            SetVoiceControlFromPreset(CompressorThresholdSlider, CompressorThresholdValueBox, preset, "Compressor");
            SetVoiceControlFromPreset(CompressionRatioSlider, CompressionRatioValueBox, preset, "CompressionRatio");
            SetVoiceControlFromPreset(LimiterSlider, LimiterValueBox, preset, "Limiter");
            SetVoiceControlFromPreset(RobotSlider, RobotValueBox, preset, "Robot");
            SetVoiceControlFromPreset(RadioSlider, RadioValueBox, preset, "Radio");
            SetVoiceControlFromPreset(ReverbSlider, ReverbValueBox, preset, "Reverb");
            SetVoiceControlFromPreset(BrightnessSlider, BrightnessValueBox, preset, "Brightness");
        }
        finally
        {
            _loadingVoicePreset = false;
        }

        UpdateVoiceSettingLabels();
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
        if (slider == InputGainSlider) return InputGainValueBox;
        if (slider == VoiceGainSlider) return VoiceGainValueBox;
        if (slider == PitchSlider) return PitchValueBox;
        if (slider == FormantSlider) return FormantValueBox;
        if (slider == GateThresholdSlider) return GateThresholdValueBox;
        if (slider == CompressorThresholdSlider) return CompressorThresholdValueBox;
        if (slider == CompressionRatioSlider) return CompressionRatioValueBox;
        if (slider == LimiterSlider) return LimiterValueBox;
        if (slider == RobotSlider) return RobotValueBox;
        if (slider == RadioSlider) return RadioValueBox;
        if (slider == ReverbSlider) return ReverbValueBox;
        if (slider == BrightnessSlider) return BrightnessValueBox;
        return null;
    }

    private Slider? GetVoiceSliderForTextBox(TextBox textBox)
    {
        if (textBox == InputGainValueBox) return InputGainSlider;
        if (textBox == VoiceGainValueBox) return VoiceGainSlider;
        if (textBox == PitchValueBox) return PitchSlider;
        if (textBox == FormantValueBox) return FormantSlider;
        if (textBox == GateThresholdValueBox) return GateThresholdSlider;
        if (textBox == CompressorThresholdValueBox) return CompressorThresholdSlider;
        if (textBox == CompressionRatioValueBox) return CompressionRatioSlider;
        if (textBox == LimiterValueBox) return LimiterSlider;
        if (textBox == RobotValueBox) return RobotSlider;
        if (textBox == RadioValueBox) return RadioSlider;
        if (textBox == ReverbValueBox) return ReverbSlider;
        if (textBox == BrightnessValueBox) return BrightnessSlider;
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
        if (VoiceInputGainLabel is null || VoiceGainLabel is null || PitchLabel is null || FormantLabel is null ||
            GateThresholdLabel is null || CompressorThresholdLabel is null || CompressionRatioLabel is null || LimiterLabel is null ||
            RobotLabel is null || RadioLabel is null || ReverbLabel is null || BrightnessLabel is null ||
            InputGainSlider is null || VoiceGainSlider is null || PitchSlider is null || FormantSlider is null ||
            GateThresholdSlider is null || CompressorThresholdSlider is null || CompressionRatioSlider is null || LimiterSlider is null ||
            RobotSlider is null || RadioSlider is null || ReverbSlider is null || BrightnessSlider is null) return;
        VoiceInputGainLabel.Text = "Input Gain";
        VoiceGainLabel.Text = "Voice Gain";
        PitchLabel.Text = "Pitch";
        FormantLabel.Text = "Formant";
        GateThresholdLabel.Text = "Gate";
        CompressorThresholdLabel.Text = "Compression Threshold";
        CompressionRatioLabel.Text = "Compression Ratio";
        LimiterLabel.Text = "Limiter";
        RobotLabel.Text = "Robot";
        RadioLabel.Text = "Radio";
        ReverbLabel.Text = "Reverb";
        BrightnessLabel.Text = "Brightness";
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
        _settings.VoiceInputGain = GetVoiceValue(InputGainSlider, InputGainValueBox);
        _settings.VoiceGain = GetVoiceValue(VoiceGainSlider, VoiceGainValueBox);
        _settings.VoicePitch = GetVoiceValue(PitchSlider, PitchValueBox);
        _settings.VoiceFormant = GetVoiceValue(FormantSlider, FormantValueBox);
        _settings.VoiceGate = GetVoiceValue(GateThresholdSlider, GateThresholdValueBox);
        _settings.VoiceCompressor = GetVoiceValue(CompressorThresholdSlider, CompressorThresholdValueBox);
        _settings.VoiceCompressionRatio = GetVoiceValue(CompressionRatioSlider, CompressionRatioValueBox);
        _settings.VoiceLimiter = GetVoiceValue(LimiterSlider, LimiterValueBox);
        _settings.VoiceRobot = GetVoiceValue(RobotSlider, RobotValueBox);
        _settings.VoiceRadio = GetVoiceValue(RadioSlider, RadioValueBox);
        _settings.VoiceReverb = GetVoiceValue(ReverbSlider, ReverbValueBox);
        _settings.VoiceBrightness = GetVoiceValue(BrightnessSlider, BrightnessValueBox);

        // Keep legacy dB fields meaningful for older settings readers.
        _settings.VoiceGainDb = MapCentered(_settings.VoiceGain, 0, -24, 12);
        _settings.GateThresholdDb = MapCentered(_settings.VoiceGate, -45, -70, -20);
        _settings.CompressorThresholdDb = MapCentered(_settings.VoiceCompressor, -18, -40, 0);

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
