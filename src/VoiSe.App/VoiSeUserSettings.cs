namespace VoiSe.App;

public sealed class VoiSeUserSettings
{
    public int SchemaVersion { get; set; } = 2;

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

    public double VoiceGainDb { get; set; } = 0.0;
    public double GateThresholdDb { get; set; } = -45.0;
    public double CompressorThresholdDb { get; set; } = -18.0;
}
