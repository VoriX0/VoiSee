using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiSe.App;

public sealed class AiVoiceLibraryItem
{
    public string Provider { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Engine { get; set; } = "Unknown";
    public string Compatibility { get; set; } = "Not supported";
    public long Downloads { get; set; }
    public int Likes { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ModelFiles { get; set; } = Array.Empty<string>();
    public Uri? BrowserUri { get; set; }

    public bool IsDownloadable => ModelFiles.Count > 0 && !string.Equals(Compatibility, "Not supported", StringComparison.OrdinalIgnoreCase);
}

public interface IAiVoiceLibraryProvider
{
    string Name { get; }
    Uri GetBrowserUri(string query, string filter);
    Task<IReadOnlyList<AiVoiceLibraryItem>> SearchAsync(string query, string filter, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> DownloadModelAssetsAsync(AiVoiceLibraryItem item, string destinationDirectory, CancellationToken cancellationToken);
}
