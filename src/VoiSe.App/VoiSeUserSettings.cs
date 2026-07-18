namespace VoiSe.App;

public sealed class VoiSeUserSettings
{
    public int SchemaVersion { get; set; } = 6;

    public string? InputDeviceId { get; set; }
    public string? InputDeviceName { get; set; }
    public string? VirtualOutputDeviceId { get; set; }
    public string? VirtualOutputDeviceName { get; set; }
    public string? MonitorOutputDeviceId { get; set; }
    public string? MonitorOutputDeviceName { get; set; }

    // Kept for migration from Gates 3/4. Gate 5 uses LastSoundId.
    public string? LastSoundFilePath { get; set; }
    public string? LastSoundId { get; set; }
    public string? LastSoundCategoryId { get; set; }

    public double VirtualMicMasterVolume { get; set; } = 1.0;
    public bool VoiceMonitorEnabled { get; set; } = false;
    public double SoundBoardVirtualMicVolume { get; set; } = 1.0;
    public double SoundBoardHeadphonesVolume { get; set; } = 1.0;
    public double SoundBoardVirtualMicDelayMs { get; set; } = 85.0;

    // Legacy dB values are kept for backward compatibility with Gate 4/5 settings files.
    public double VoiceGainDb { get; set; } = 0.0;
    public double GateThresholdDb { get; set; } = -45.0;
    public double CompressorThresholdDb { get; set; } = -18.0;

    // Gate 6 voice controls use a normalized -100..+100 UI scale.
    public double VoiceInputGain { get; set; } = 0.0;
    public double VoiceGain { get; set; } = 0.0;
    public double VoicePitch { get; set; } = 0.0;
    public double VoiceFormant { get; set; } = 0.0;
    public double VoiceGate { get; set; } = 0.0;
    public double VoiceCompressor { get; set; } = 0.0;
    public double VoiceCompressionRatio { get; set; } = 0.0; // legacy / ignored from Gate 6.2+
    public double VoiceLimiter { get; set; } = 0.0; // legacy / ignored from Gate 6.2+
    public double VoiceRobot { get; set; } = 0.0;
    public double VoiceRadio { get; set; } = 0.0;
    public double VoiceReverb { get; set; } = 0.0;
    public double VoiceBrightness { get; set; } = 0.0; // legacy / ignored from Gate 6.2+

    // Gate 6.5 connected DSP controls.
    public double VoiceTimbre { get; set; } = 0.0; // legacy / ignored from Gate 6.5+
    public double VoiceBass { get; set; } = 0.0;
    public double VoiceTreble { get; set; } = 0.0;
    public double VoiceDistortion { get; set; } = 0.0;
    public double VoiceTremolo { get; set; } = 0.0;
    public double VoiceEcho { get; set; } = 0.0;
    public double VoiceBitCrusher { get; set; } = 0.0;
    public double VoiceChorus { get; set; } = 0.0; // legacy / ignored from Gate 6.5+
    public double VoiceAlien { get; set; } = 0.0;


    // Gate 6.17 global transport hotkeys. SoundBoard sound hotkeys live in soundboard.json;
    // voice preset hotkeys live in individual preset JSON files.
    public string? SoundBoardPlayHotkey { get; set; }
    public string? SoundBoardPauseHotkey { get; set; }
    public string? SoundBoardStopHotkey { get; set; }
    public string? SoundBoardNextHotkey { get; set; }
    public string? SoundBoardPreviousHotkey { get; set; }
    public string? DisableSceneHotkey { get; set; }
    public string? VirtualMicMuteHotkey { get; set; }

    // VoiSee 11 Media Bridge. The profile is descriptive only: no PID or HWND is persisted,
    // and capture never reconnects automatically after application restart.
    public string? MediaBridgeLastProcessName { get; set; }
    public string? MediaBridgeLastWindowTitle { get; set; }
    public double MediaBridgeVirtualMicVolume { get; set; } = 1.0;
    public string? MediaBridgePauseHotkey { get; set; }

    // VoiSee 10.1 native XAML ResourceDictionary theme. Null means built-in Default Dark.
    public string? ThemeFilePath { get; set; }
}

