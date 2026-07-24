using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiSe.App;

public sealed class VoicePreset
{
    public int SchemaVersion { get; set; } = 2;
    public string Name { get; set; } = "Preset";
    public string Icon { get; set; } = "\uE720";
    public Dictionary<string, double> Sliders { get; set; } = new();
    public List<string> EffectOrder { get; set; } = new();
    public string? PushToTalkHotkey { get; set; }
    public string? PresetHotkey { get; set; }

    [JsonIgnore]
    public string? FilePath { get; set; }
}
