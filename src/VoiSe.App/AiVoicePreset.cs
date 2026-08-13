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
    public string? QuickProfileEntry { get; set; }
    public double? ReferenceDurationSeconds { get; set; }
    public DateTimeOffset? QuickPreparedUtc { get; set; }
    public string? Notes { get; set; }

    // Runtime-only path to the installed .vsee package.
    [JsonIgnore]
    public string? FilePath { get; set; }

    [JsonIgnore]
    public bool IsReference => string.Equals(VoiceType, "Reference", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsQuickClone => string.Equals(VoiceType, "Quick Clone", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsOptimized => string.Equals(VoiceType, "Optimized", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool CanQuickClone => IsReference && !string.IsNullOrWhiteSpace(ReferenceAudioEntry);

    [JsonIgnore]
    public bool CanOptimize => IsReference || IsQuickClone;

    // 13.5 activation gate: imported RVC packages and prepared Quick/Optimized voices
    // can be selected as the active AI Voice. Realtime inference is connected later.
    [JsonIgnore]
    public bool CanActivate =>
        (string.Equals(Engine, "RVC", StringComparison.OrdinalIgnoreCase)
         && AssetEntries.Exists(entry => entry.EndsWith(".pth", StringComparison.OrdinalIgnoreCase)))
        || IsQuickClone
        || IsOptimized;
}
