using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiSe.App;

public sealed class AiVoicePreset
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "AI Voice";
    public string VoiceType { get; set; } = "Stored";
    public string Engine { get; set; } = "Unknown";
    public string Compatibility { get; set; } = "Stored";
    public string Source { get; set; } = "Local";
    public double? EstimatedAiLatencyMs { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<string> AssetEntries { get; set; } = new();
    public string? ReferenceAudioEntry { get; set; }
    public string? Notes { get; set; }

    // Runtime-only path to the installed .vsee package.
    [JsonIgnore]
    public string? FilePath { get; set; }
}
