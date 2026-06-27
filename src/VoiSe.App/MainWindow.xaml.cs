using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    private VoiSeUserSettings _settings;
    private SoundBoardLibrary _library;
    private IReadOnlyList<VoicePreset> _voicePresets = Array.Empty<VoicePreset>();
    private IReadOnlyList<VoiSeScene> _scenes = Array.Empty<VoiSeScene>();
    private VoiSeScene? _selectedScene;
    private string? _activeSceneId;
    private string? _lastAppliedVoicePresetName;
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
    private const double VoiceValueMin = -9999.0;
    private const double VoiceValueMax = 9999.0;
    private const double SceneSoundButtonWidth = 154.0;
    private const double SceneSoundButtonHeight = 58.0;
    private bool _loadingSceneUi;

    public MainWindow()
    {
        StartupLog.Write("MainWindow constructor started.");
        _settings = _settingsStore.Load();
        _libraryStore = new SoundBoardLibraryStore(_settingsStore.DataDirectory);
        _voicePresetStore = new VoicePresetStore(_settingsStore.DataDirectory);
        _sceneStore = new SceneStore(_settingsStore.DataDirectory);
        _library = _libraryStore.Load();
        InitializeComponent();
        _windowHandle = WindowNative.GetWindowHandle(this);
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

        AppendLog("Gate 7.2 UI started.");
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
        }
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

        return MainTabView.SelectedIndex switch
        {
            0 => IsPointInSoundBoardWheelZone(xDip, yDip) && TryScrollSoundOverlay(delta),
            1 => IsPointInVoiceChangerWheelZone(yDip) && TryScrollVoiceChanger(delta),
            3 => false,
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
        if (VoiceChangerScrollViewer is null || wheelDelta == 0)
        {
            return false;
        }

        var notches = Math.Max(1.0, Math.Abs(wheelDelta) / 120.0);
        var step = 42.0 * notches;
        var target = VoiceChangerScrollViewer.VerticalOffset - Math.Sign(wheelDelta) * step;
        target = Math.Max(0, Math.Min(VoiceChangerScrollViewer.ScrollableHeight, target));
        VoiceChangerScrollViewer.ChangeView(null, target, null, disableAnimation: false);
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
                Height = Math.Max(560, RootGrid?.ActualHeight * 0.78 ?? 560),
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                VerticalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                ZoomMode = ZoomMode.Disabled
            };

            var dialog = new ContentDialog
            {
                Title = "Application log",
                Content = scrollViewer,
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };

            await dialog.ShowAsync();
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

        if (TryHandleTransportHotkey(current)) return true;
        if (TryHandleSceneHotkey(current)) return true;
        if (TryHandleSoundHotkey(current)) return true;
        if (TryHandleVoicePresetHotkey(current)) return true;

        return false;
    }

    private bool TryHandleSceneHotkey(HotkeyGesture current)
    {
        if (string.IsNullOrWhiteSpace(_activeSceneId))
        {
            return false;
        }

        var activeScene = _scenes.FirstOrDefault(scene => scene.Id == _activeSceneId);
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
                SelectSound(sound);
                PlaySelectedSound();
                AppendLog($"Scene hotkey: {activeScene.Name} / {displayName} [{configured}]");
            });
            return true;
        }

        return false;
    }

    private bool TryHandleSoundHotkey(HotkeyGesture current)
    {
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
        if (HotkeyGesture.TryParse(_settings.SoundBoardPlayHotkey, out var playPause) && playPause.Equals(current))
        {
            DispatcherQueue.TryEnqueue(TransportPlayPause);
            return true;
        }

        // Legacy fallback: old settings files may still contain a separate Pause hotkey.
        if (HotkeyGesture.TryParse(_settings.SoundBoardPauseHotkey, out var legacyPause) && legacyPause.Equals(current))
        {
            DispatcherQueue.TryEnqueue(TransportPlayPause);
            return true;
        }

        if (HotkeyGesture.TryParse(_settings.SoundBoardStopHotkey, out var stop) && stop.Equals(current))
        {
            DispatcherQueue.TryEnqueue(TransportStop);
            return true;
        }

        if (HotkeyGesture.TryParse(_settings.SoundBoardNextHotkey, out var next) && next.Equals(current))
        {
            DispatcherQueue.TryEnqueue(() => SelectRelativeSound(1, play: true));
            return true;
        }

        if (HotkeyGesture.TryParse(_settings.SoundBoardPreviousHotkey, out var previous) && previous.Equals(current))
        {
            DispatcherQueue.TryEnqueue(() => SelectRelativeSound(-1, play: true));
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
            Text = "Click a hotkey button, then press a key or Ctrl/Alt/Shift combination. Esc cancels capture. Plain A-Z and < > { } are local-only; Ctrl/Alt/Shift combinations remain global.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });

        panel.Children.Add(CreateHotkeyCaptureRow("Play / Pause", _settings.SoundBoardPlayHotkey, out var playPauseButton));
        panel.Children.Add(CreateHotkeyCaptureRow("Stop", _settings.SoundBoardStopHotkey, out var stopButton));
        panel.Children.Add(CreateHotkeyCaptureRow("Next", _settings.SoundBoardNextHotkey, out var nextButton));
        panel.Children.Add(CreateHotkeyCaptureRow("Previous", _settings.SoundBoardPreviousHotkey, out var previousButton));

        var dialog = new ContentDialog
        {
            Title = "Transport hotkeys",
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
        _settingsStore.Save(_settings);
        UpdateTransportHotkeySummary();
        AppendLog("Transport hotkeys updated.");
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
            $"Prev: {(_settings.SoundBoardPreviousHotkey ?? "—")}"
        };
        TransportHotkeysSummaryTextBlock.Text = "Transport hotkeys: " + string.Join("    ", parts);
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

            if (upper.StartsWith("F") && int.TryParse(upper[1..], out var fn) && fn is >= 1 and <= 24)
            {
                keyCode = 0x70 + fn - 1;
                return true;
            }

            keyCode = upper switch
            {
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
            RebuildSceneSoundButtons();
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
        var hotkey = await CaptureHotkeyDialogAsync(
            "Assign sound hotkey",
            "Click the hotkey button, then press a key or Ctrl/Alt/Shift combination. Esc cancels capture. Plain A-Z and < > { } are local-only; Ctrl/Alt/Shift combinations remain global.",
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

    private void PlaySelectedSound(bool loop = false)
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
            _currentSoundDisplayName = _selectedSound?.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(soundPath);
            _engine.PlaySound(soundPath, virtualVolume, monitorVolume, delayMs, loop);
            if (_selectedSound is not null)
            {
                _libraryStore.IncrementUsage(_library, _selectedSound);
            }
            UpdateBottomStats();
            AppendLog($"Sound started{(loop ? " in loop" : string.Empty)}: {_selectedSound?.DisplayName ?? System.IO.Path.GetFileName(soundPath)}. Virtual delay: {delayMs} ms.");
        }
        catch (Exception ex)
        {
            AppendLog($"Sound playback error: {ex.Message}");
        }
    }

    private void OnStopSoundClick(object sender, RoutedEventArgs e)
    {
        _engine?.StopSound();
        _currentSoundDisplayName = null;
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
        RebuildSceneSoundButtons();
    }

    private void SetSceneEditorEnabled(bool enabled)
    {
        if (SceneVoicePresetComboBox is not null) SceneVoicePresetComboBox.IsEnabled = enabled;
        if (SceneVoicePresetClearButton is not null) SceneVoicePresetClearButton.IsEnabled = enabled;
        if (SceneVoicePresetCreateButton is not null) SceneVoicePresetCreateButton.IsEnabled = enabled;
        if (SceneVoiceMonitorButton is not null) SceneVoiceMonitorButton.IsEnabled = enabled;
        if (SceneAutostartLoopsCheckBox is not null) SceneAutostartLoopsCheckBox.IsEnabled = enabled;
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

    private string CreateSceneDetailsText(VoiSeScene scene)
    {
        var normalCount = scene.SoundButtons.Count(b => !b.IsLooped);
        var hasLoop = scene.SoundButtons.Any(b => b.IsLooped);
        return string.Join(Environment.NewLine,
            $"Voice preset: {scene.VoicePresetName ?? "none"}",
            $"Scene buttons: {normalCount}",
            $"Looped sound: {(hasLoop ? "set" : "none")}",
            $"Autostart loop: {(scene.AutoStartLoopedSounds ? "on" : "off")}",
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
            scene.SoundButtons.Clear();
            scene.AutoStartLoopedSounds = false;
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
        _activeSceneId = null;
        UpdateSceneActiveFlags();
        RefreshSceneListBinding();
        TransportStop();
        AppendLog("Scenes disabled.");
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

            var updated = CaptureCurrentScene(_selectedScene.Name);
            updated.Id = _selectedScene.Id;
            updated.Icon = _selectedScene.Icon;
            updated.CreatedAtUtc = _selectedScene.CreatedAtUtc;
            updated.FilePath = _selectedScene.FilePath;
            updated.SoundButtons = previousButtons.Count == 0 ? updated.SoundButtons : previousButtons;
            updated.AutoStartLoopedSounds = previousAutostartLoops;
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

            var firstButtonSound = scene.SoundButtons
                .OrderBy(b => b.SortOrder)
                .Select(b => PickSound(b.SoundId))
                .FirstOrDefault(s => s is not null);

            var category = PickCategory(firstButtonSound?.CategoryId ?? scene.SoundCategoryId)
                ?? _library.Categories.FirstOrDefault(c => string.Equals(c.Name, scene.SoundCategoryName, StringComparison.CurrentCultureIgnoreCase));
            if (category is not null)
            {
                CategoryComboBox.SelectedItem = category;
                RefreshSoundList();
            }

            var sound = firstButtonSound
                ?? PickSound(scene.BackgroundSoundId)
                ?? _library.Sounds.FirstOrDefault(s => string.Equals(s.DisplayName, scene.BackgroundSoundName, StringComparison.CurrentCultureIgnoreCase));
            if (sound is not null)
            {
                var soundCategory = PickCategory(sound.CategoryId);
                if (soundCategory is not null && !ReferenceEquals(CategoryComboBox.SelectedItem, soundCategory))
                {
                    CategoryComboBox.SelectedItem = soundCategory;
                    RefreshSoundList();
                }

                SelectSound(sound);
            }

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
                var loopedSound = scene.SoundButtons
                    .Where(b => b.IsLooped)
                    .OrderBy(b => b.SortOrder)
                    .Select(b => PickSound(b.SoundId))
                    .FirstOrDefault(s => s is not null);
                if (loopedSound is not null)
                {
                    SelectSound(loopedSound);
                    PlaySelectedSound(loop: true);
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


    private void UpdateSceneActiveFlags()
    {
        foreach (var scene in _scenes)
        {
            scene.IsActive = !string.IsNullOrWhiteSpace(_activeSceneId)
                && string.Equals(scene.Id, _activeSceneId, StringComparison.OrdinalIgnoreCase);
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
            IsLooped = source.IsLooped,
            SortOrder = source.SortOrder
        };
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

        if (_selectedScene is null)
        {
            if (SceneLoopedEmptyTextBlock is not null)
            {
                SceneLoopedEmptyTextBlock.Visibility = Visibility.Visible;
                SceneLoopedEmptyTextBlock.Text = "No scene selected.";
            }
            RefreshSceneLoopActionButtons();
            return;
        }

        var orderedButtons = _selectedScene.SoundButtons
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.LocalName)
            .ToList();

        var loopedButton = orderedButtons.FirstOrDefault(b => b.IsLooped);
        if (loopedButton is not null)
        {
            LoopedSceneSoundsPanel.Children.Add(CreateLoopedSceneSoundButton(loopedButton));
        }

        foreach (var sceneButton in orderedButtons.Where(b => !b.IsLooped))
        {
            SceneSoundsPanel.Children.Add(CreateSceneSoundButton(sceneButton));
        }

        SceneSoundsPanel.Children.Add(CreateSceneAddSoundButton());

        if (SceneLoopedEmptyTextBlock is not null)
        {
            SceneLoopedEmptyTextBlock.Visibility = loopedButton is null ? Visibility.Visible : Visibility.Collapsed;
            SceneLoopedEmptyTextBlock.Text = "No looped sound in this scene.";
        }

        RefreshSceneLoopActionButtons();
    }

    private void RefreshSceneLoopActionButtons()
    {
        var hasScene = _selectedScene is not null;
        var hasLoopedSound = GetSelectedSceneLoopedButton() is not null;
        if (SceneLoopPlayLoopButton is not null) SceneLoopPlayLoopButton.IsEnabled = hasLoopedSound;
        if (SceneLoopPlayOnceButton is not null) SceneLoopPlayOnceButton.IsEnabled = hasLoopedSound;
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
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private Button CreateLoopedSceneSoundButton(SceneSoundButton sceneButton)
    {
        var sound = PickSound(sceneButton.SoundId);
        var context = new SceneSoundButtonContext
        {
            Scene = _selectedScene!,
            Button = sceneButton,
            Sound = sound
        };

        var button = CreateSceneButtonShell();
        button.Width = double.NaN;
        button.Margin = new Thickness(0, 0, 0, 0);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Tag = context;
        button.Content = new TextBlock
        {
            Text = context.DisplayName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.ContextFlyout = CreateSceneSoundButtonFlyout(context);
        button.Click += OnSceneSoundButtonClick;
        return button;
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
        var stack = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };

        stack.Children.Add(new TextBlock
        {
            Text = context.DisplayName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
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

        stack.Children.Add(new TextBlock
        {
            Text = hotkeyParts.Count == 0 ? " " : string.Join("  ", hotkeyParts),
            FontSize = 11,
            Opacity = 0.68,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        return stack;
    }

    private Button CreateSceneAddSoundButton()
    {
        var button = CreateSceneButtonShell();
        button.Content = new TextBlock
        {
            Text = "+",
            FontSize = 30,
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
            IsLooped = false,
            SortOrder = nextOrder
        });

        SaveSelectedSceneEditorChange($"Scene sound added: {sound.DisplayName}");
    }

    private MenuFlyout CreateSceneSoundButtonFlyout(SceneSoundButtonContext context)
    {
        var flyout = new MenuFlyout();

        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = $"SoundBoard: {context.SourceName}",
            IsEnabled = false
        });
        flyout.Items.Add(new MenuFlyoutSeparator());

        var rename = new MenuFlyoutItem { Text = "Rename" };
        rename.Click += async (_, _) => await RenameSceneSoundButtonAsync(context.Button);
        flyout.Items.Add(rename);

        var chooseAnother = new MenuFlyoutItem { Text = "Choose another sound" };
        chooseAnother.Click += async (_, _) => await ChooseAnotherSceneSoundAsync(context.Button);
        flyout.Items.Add(chooseAnother);

        var delete = new MenuFlyoutItem { Text = "Delete" };
        delete.Click += (_, _) => DeleteSceneSoundButton(context.Button);
        flyout.Items.Add(delete);

        var hotkeyText = string.IsNullOrWhiteSpace(context.Button.SceneHotkey)
            ? "Scene hotkey"
            : $"Scene hotkey: {context.Button.SceneHotkey}";
        var hotkey = new MenuFlyoutItem
        {
            Text = hotkeyText,
            IsEnabled = context.Sound is not null
        };
        hotkey.Click += async (_, _) => await EditSceneSoundHotkeyAsync(context.Button);
        flyout.Items.Add(hotkey);

        return flyout;
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

        SelectSound(context.Sound);
        PlaySelectedSound();
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
        PlaySceneLoopedSound(loop: true);
    }

    private void OnSceneLoopPlayOnceClick(object sender, RoutedEventArgs e)
    {
        PlaySceneLoopedSound(loop: false);
    }

    private void OnSceneLoopRemoveClick(object sender, RoutedEventArgs e)
    {
        var loopedButton = GetSelectedSceneLoopedButton();
        if (_selectedScene is null || loopedButton is null)
        {
            return;
        }

        var soundName = PickSound(loopedButton.SoundId)?.DisplayName ?? loopedButton.LocalName ?? loopedButton.SoundId;
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
                LocalName = sound.DisplayName,
                IsLooped = true,
                SortOrder = nextOrder
            });
        }
        else
        {
            loopedButton.SoundId = sound.Id;
            loopedButton.LocalName = sound.DisplayName;
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

        SelectSound(sound);
        PlaySelectedSound(loop);
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
        var panel = new StackPanel
        {
            Spacing = 12
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Click a hotkey button, then press a key or Ctrl/Alt/Shift combination. Esc cancels capture. Plain A-Z and < > { } are local-only; Ctrl/Alt/Shift combinations remain global.",
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

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (_logBuffer.Length > 0)
        {
            _logBuffer.AppendLine();
        }

        _logBuffer.Append(line);
    }
}
