using System.Collections.Generic;

namespace VoiSe.App;

public sealed class VoicePreset
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Preset";
    public string Icon { get; set; } = "🎙️";
    public Dictionary<string, double> Sliders { get; set; } = new();
}
