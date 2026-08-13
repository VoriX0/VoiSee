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
    public string? OptimizedProfileEntry { get; set; }
    public string? OptimizedReferenceEntry { get; set; }
    public double? ReferenceDurationSeconds { get; set; }
    public double? OptimizedReferenceDurationSeconds { get; set; }
    public string? ReferenceQuality { get; set; }
    public DateTimeOffset? QuickPreparedUtc { get; set; }
    public DateTimeOffset? OptimizedPreparedUtc { get; set; }
    public double? OptimizationDurationMs { get; set; }
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

    // VoiSee 13.7 first realtime backend: imported/downloaded RVC .pth packages
    // can drive the live microphone path. Quick/Optimized reference voices keep
    // their lifecycle state but wait for the zero-shot runtime Gate.
    [JsonIgnore]
    public bool CanActivate =>
        string.Equals(Engine, "RVC", StringComparison.OrdinalIgnoreCase)
        && AssetEntries.Exists(entry => entry.EndsWith(".pth", StringComparison.OrdinalIgnoreCase));
}
