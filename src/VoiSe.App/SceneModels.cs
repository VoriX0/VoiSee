using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace VoiSe.App;

public sealed class VoiSeScene
{
    public int SchemaVersion { get; set; } = 7;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Scene";
    public string Icon { get; set; } = "🎬";

    public string? VoicePresetName { get; set; }
    public Dictionary<string, double> VoiceSliders { get; set; } = new();
    public bool VoiceMonitorEnabled { get; set; }

    // Gate 7.0 compatibility fields. New scene sound buttons live in SoundButtons.
    public string? SoundCategoryId { get; set; }
    public string? SoundCategoryName { get; set; }
    public string? BackgroundSoundId { get; set; }
    public string? BackgroundSoundName { get; set; }
    public List<string> OneShotSoundIds { get; set; } = new();

    public List<SceneSoundButton> SoundButtons { get; set; } = new();
    public bool AutoStartLoopedSounds { get; set; }

    public double LoopedSoundVirtualMicVolume { get; set; } = 1.0;
    public double LoopedSoundHeadphonesVolume { get; set; } = 1.0;
    public double SceneButtonsVirtualMicVolume { get; set; } = 1.0;
    public double SceneButtonsHeadphonesVolume { get; set; } = 1.0;

    public string? StopOneShotSoundsHotkey { get; set; }
    public string? PauseOneShotSoundsHotkey { get; set; }
    public string? DisableSceneHotkey { get; set; }

    public double VirtualMicMasterVolume { get; set; } = 1.0;
    public double SoundBoardVirtualMicVolume { get; set; } = 1.0;
    public double SoundBoardHeadphonesVolume { get; set; } = 1.0;
    public double SoundBoardVirtualMicDelayMs { get; set; } = 85.0;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? FilePath { get; set; }

    [JsonIgnore]
    public bool IsActive { get; set; }

    [JsonIgnore]
    public string ActiveBadge => IsActive ? "ACTIVE" : string.Empty;

    [JsonIgnore]
    public double ActiveOpacity => IsActive ? 1.0 : 0.0;

    [JsonIgnore]
    public string SceneListSubtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(VoicePresetName))
            {
                parts.Add($"Voice: {VoicePresetName}");
            }

            var normalCount = SoundButtons.Count(b => !b.IsLooped);
            var loop = SoundButtons.FirstOrDefault(b => b.IsLooped);
            parts.Add($"Sounds: {normalCount}");
            parts.Add(loop is null ? "Loop: none" : "Loop: set");
            if (IsActive)
            {
                parts.Insert(0, "ACTIVE");
            }

            return string.Join(" · ", parts);
        }
    }
}

public sealed class SceneSoundButton
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SoundId { get; set; } = string.Empty;
    public string? LocalName { get; set; }
    public string? SceneHotkey { get; set; }
    public double VirtualMicVolume { get; set; } = 1.0;
    public double HeadphonesVolume { get; set; } = 1.0;
    public bool IsLooped { get; set; }
    public int SortOrder { get; set; }
}
