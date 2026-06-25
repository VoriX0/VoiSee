using System;
using System.Collections.Generic;

namespace VoiSe.App;

public sealed class SoundBoardLibrary
{
    public int SchemaVersion { get; set; } = 1;
    public List<SoundBoardCategory> Categories { get; set; } = new();
    public List<SoundBoardSound> Sounds { get; set; } = new();
}

public sealed class SoundBoardCategory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default";
    public int SortOrder { get; set; }
}

public sealed class SoundBoardSound
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? OriginalFileName : Name;
}
