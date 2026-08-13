using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoiSe.App;

public sealed class HuggingFaceVoiceLibraryProvider : IAiVoiceLibraryProvider, IDisposable
{
    private const string HubBase = "https://huggingface.co";
    private readonly HttpClient _httpClient;

    public HuggingFaceVoiceLibraryProvider()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VoiSee/13.7.0");
    }

    public string Name => "Hugging Face";

    public Uri GetBrowserUri(string query, string filter)
    {
        var search = BuildSearchText(query, filter);
        return new Uri($"{HubBase}/models?search={Uri.EscapeDataString(search)}");
    }

    public async Task<IReadOnlyList<AiVoiceLibraryItem>> SearchAsync(
        string query,
        string filter,
        CancellationToken cancellationToken)
    {
        var search = BuildSearchText(query, filter);
        var requestUri = $"{HubBase}/api/models?search={Uri.EscapeDataString(search)}&sort=downloads&direction=-1&limit=60&full=true";

        using var response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AiVoiceLibraryItem>();
        }

        var items = new List<AiVoiceLibraryItem>();
        foreach (var model in document.RootElement.EnumerateArray())
        {
            var repositoryId = GetString(model, "id") ?? GetString(model, "modelId");
            if (string.IsNullOrWhiteSpace(repositoryId)) continue;

            var tags = GetStringArray(model, "tags");
            var siblings = GetSiblingNames(model);
            var modelFiles = SelectSupportedAssets(siblings);
            var compatibility = DetectCompatibility(modelFiles);

            // The Hub search term "rvc" can also match unrelated scientific acronyms.
            // Only surface repositories that look like voice-conversion models by tags/name/files.
            if (!LooksLikeVoiceConversion(repositoryId, tags, modelFiles))
            {
                continue;
            }

            var slash = repositoryId.IndexOf('/');
            var author = slash > 0 ? repositoryId[..slash] : string.Empty;
            var name = slash >= 0 && slash + 1 < repositoryId.Length ? repositoryId[(slash + 1)..] : repositoryId;
            var engine = modelFiles.Any(f => f.EndsWith(".pth", StringComparison.OrdinalIgnoreCase))
                ? "RVC"
                : modelFiles.Any(f => f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) ? "ONNX" : "Unknown";

            items.Add(new AiVoiceLibraryItem
            {
                Provider = Name,
                RepositoryId = repositoryId,
                DisplayName = MakeDisplayName(name),
                Author = author,
                Engine = engine,
                Compatibility = compatibility,
                Downloads = GetInt64(model, "downloads"),
                Likes = (int)Math.Min(int.MaxValue, GetInt64(model, "likes")),
                Tags = tags,
                ModelFiles = modelFiles,
                BrowserUri = new Uri($"{HubBase}/{repositoryId}")
            });
        }

        return ApplyClientFilter(items, filter).Take(30).ToList();
    }

    public async Task<IReadOnlyList<string>> DownloadModelAssetsAsync(
        AiVoiceLibraryItem item,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        if (string.IsNullOrWhiteSpace(item.RepositoryId)) throw new InvalidDataException("Repository ID is missing.");
        if (!item.IsDownloadable) throw new InvalidDataException("This model is not directly downloadable by the current VoiSee provider.");

        Directory.CreateDirectory(destinationDirectory);
        var downloaded = new List<string>();
        foreach (var remoteFile in PickDownloadAssets(item.ModelFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var encodedPath = string.Join("/", remoteFile.Split('/').Select(Uri.EscapeDataString));
            var url = $"{HubBase}/{item.RepositoryId}/resolve/main/{encodedPath}?download=true";
            var localName = Path.GetFileName(remoteFile);
            if (string.IsNullOrWhiteSpace(localName)) continue;
            var localPath = MakeAvailableFilePath(destinationDirectory, localName);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(localPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
            await source.CopyToAsync(destination, 1024 * 1024, cancellationToken);
            downloaded.Add(localPath);
        }

        if (downloaded.Count == 0)
        {
            throw new InvalidDataException("No supported RVC/ONNX assets were found in this repository.");
        }

        return downloaded;
    }

    private static string BuildSearchText(string query, string filter)
    {
        // Hub `search` is a model-id text search, not a structured multi-tag query.
        // Use the user's text verbatim when present; otherwise seed discovery with one useful term.
        if (!string.IsNullOrWhiteSpace(query)) return query.Trim();
        if (string.Equals(filter, "Male", StringComparison.OrdinalIgnoreCase)) return "male";
        if (string.Equals(filter, "Female", StringComparison.OrdinalIgnoreCase)) return "female";
        if (string.Equals(filter, "Character", StringComparison.OrdinalIgnoreCase)) return "character";
        return "rvc";
    }

    private static IReadOnlyList<AiVoiceLibraryItem> ApplyClientFilter(IReadOnlyList<AiVoiceLibraryItem> items, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase)) return items;
        if (string.Equals(filter, "RVC", StringComparison.OrdinalIgnoreCase))
            return items.Where(i => string.Equals(i.Engine, "RVC", StringComparison.OrdinalIgnoreCase)).ToList();

        // Male/Female/Character are applied as Hub search terms because model-card tagging is not standardized.
        // Avoid a second strict client-side filter that would hide valid search results lacking those exact tags.
        return items;
    }

    private static bool LooksLikeVoiceConversion(string repositoryId, IReadOnlyList<string> tags, IReadOnlyList<string> files)
    {
        var id = repositoryId.ToLowerInvariant();
        var hasVoiceTag = tags.Any(t =>
            t.Contains("rvc", StringComparison.OrdinalIgnoreCase)
            || t.Contains("voice-conversion", StringComparison.OrdinalIgnoreCase)
            || t.Contains("voice_conversion", StringComparison.OrdinalIgnoreCase)
            || t.Contains("audio-to-audio", StringComparison.OrdinalIgnoreCase)
            || t.Contains("voice-changer", StringComparison.OrdinalIgnoreCase));
        var hasIndex = files.Any(f => f.EndsWith(".index", StringComparison.OrdinalIgnoreCase));
        var hasModel = files.Any(f => f.EndsWith(".pth", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
        return hasVoiceTag || (hasModel && (hasIndex || id.Contains("rvc", StringComparison.OrdinalIgnoreCase)));
    }

    private static string DetectCompatibility(IReadOnlyList<string> files)
    {
        var hasPth = files.Any(f => f.EndsWith(".pth", StringComparison.OrdinalIgnoreCase));
        var hasIndex = files.Any(f => f.EndsWith(".index", StringComparison.OrdinalIgnoreCase));
        var hasOnnx = files.Any(f => f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
        if (hasPth && hasIndex) return "RVC candidate";
        if (hasPth) return "RVC candidate · index optional";
        if (hasOnnx) return "Conversion check required";
        return "Not supported";
    }

    private static IReadOnlyList<string> SelectSupportedAssets(IReadOnlyList<string> siblings)
        => siblings.Where(f =>
                f.EndsWith(".pth", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".index", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> PickDownloadAssets(IReadOnlyList<string> assets)
    {
        var result = new List<string>();
        var pth = assets.FirstOrDefault(f => f.EndsWith(".pth", StringComparison.OrdinalIgnoreCase));
        var onnx = assets.FirstOrDefault(f => f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
        if (pth is not null)
        {
            result.Add(pth);
            result.AddRange(assets.Where(f => f.EndsWith(".index", StringComparison.OrdinalIgnoreCase)).Take(2));
        }
        else if (onnx is not null)
        {
            result.Add(onnx);
        }
        return result;
    }

    private static IReadOnlyList<string> GetSiblingNames(JsonElement model)
    {
        if (!model.TryGetProperty("siblings", out var siblings) || siblings.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var result = new List<string>();
        foreach (var sibling in siblings.EnumerateArray())
        {
            if (sibling.ValueKind == JsonValueKind.Object)
            {
                var name = GetString(sibling, "rfilename") ?? GetString(sibling, "path");
                if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
            }
            else if (sibling.ValueKind == JsonValueKind.String)
            {
                var name = sibling.GetString();
                if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
            }
        }
        return result;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return property.EnumerateArray()
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .ToList();
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static long GetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return 0;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value)) return value;
        return 0;
    }

    private static string MakeDisplayName(string name)
        => name.Replace('-', ' ').Replace('_', ' ').Trim();

    private static string MakeAvailableFilePath(string directory, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName} {suffix}{extension}");
            suffix++;
        }
        return candidate;
    }

    public void Dispose() => _httpClient.Dispose();
}
