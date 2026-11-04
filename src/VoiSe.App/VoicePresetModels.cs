using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiSe.App;

public sealed class VoicePreset
{
    // Missing SchemaVersion in exchanged presets denotes the legacy slider format.
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Preset";
    public string Icon { get; set; } = "\uE720";
    public Dictionary<string, double> Sliders { get; set; } = new();
    public List<string> EffectOrder { get; set; } = new();
    public List<string> BypassedEffects { get; set; } = new();
    public bool ChainBypassed { get; set; }
    public string? PushToTalkHotkey { get; set; }
    public string? PresetHotkey { get; set; }

    [JsonIgnore]
    public string? FilePath { get; set; }
}
