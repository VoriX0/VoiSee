using System;

namespace VoiSe.App;

public sealed class MediaBridgeProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "Media source";
    public string ProcessName { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public string? WindowTitleHint { get; set; }
    public string? BrowserFallbackUrl { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
