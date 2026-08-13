using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace VoiSe.App;

public sealed class AiVoiceStore
{
    public const string VoiceExtension = ".vsee";
    private const string ManifestEntryName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AiVoiceStore(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
        {
            throw new ArgumentException("AI Voice storage directory is required.", nameof(storageDirectory));
        }

        StorageDirectory = Path.GetFullPath(storageDirectory);
        Directory.CreateDirectory(StorageDirectory);
    }

    public string StorageDirectory { get; }

    public IReadOnlyList<AiVoicePreset> LoadVoices()
    {
        Directory.CreateDirectory(StorageDirectory);
        var voices = new List<AiVoicePreset>();
        foreach (var file in Directory.EnumerateFiles(StorageDirectory, "*" + VoiceExtension)
                     .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                voices.Add(LoadPackage(file));
            }
            catch
            {
                // A damaged or foreign package must never prevent VoiSee startup.
            }
        }

        return voices;
    }

    public AiVoicePreset LoadPackage(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("AI Voice package not found.", packagePath);
        }

        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException("The selected .vsee file does not contain manifest.json.");

        using var stream = manifestEntry.Open();
        var preset = JsonSerializer.Deserialize<AiVoicePreset>(stream, JsonOptions)
            ?? throw new InvalidDataException("The selected .vsee manifest is invalid.");

        preset.SchemaVersion = Math.Max(1, preset.SchemaVersion);
        preset.Id = string.IsNullOrWhiteSpace(preset.Id) ? Guid.NewGuid().ToString("N") : preset.Id;
        // The installed package filename is the user-facing preset name. This lets Rename stay instant
        // even for very large model packages without rewriting their payload.
        preset.Name = Path.GetFileNameWithoutExtension(packagePath);
        preset.AssetEntries ??= new List<string>();
        preset.FilePath = Path.GetFullPath(packagePath);
        return preset;
    }

    public AiVoicePreset CreateReferenceVoice(string sourceAudioPath)
    {
        if (!File.Exists(sourceAudioPath))
        {
            throw new FileNotFoundException("Reference audio file not found.", sourceAudioPath);
        }

        var suggestedName = Path.GetFileNameWithoutExtension(sourceAudioPath);
        var packagePath = GetAvailablePackagePath(suggestedName);
        var audioEntry = "reference/" + SanitizeEntryFileName(Path.GetFileName(sourceAudioPath));
        var preset = new AiVoicePreset
        {
            Name = Path.GetFileNameWithoutExtension(packagePath),
            VoiceType = "Reference",
            Engine = "Pending",
            Compatibility = "Ready for Quick Clone",
            Source = "Reference audio",
            ReferenceAudioEntry = audioEntry,
            AssetEntries = new List<string> { audioEntry },
            Notes = "Reference audio stored. AI generation is connected in a later VoiSee 13.x Gate."
        };

        WritePackage(packagePath, preset, new[] { (sourceAudioPath, audioEntry) });
        preset.FilePath = packagePath;
        return preset;
    }

    public AiVoicePreset CreateImportedModel(IReadOnlyList<string> modelFiles)
    {
        var validFiles = modelFiles.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (validFiles.Count == 0)
        {
            throw new InvalidDataException("No model files were selected.");
        }

        var primary = validFiles.FirstOrDefault(p => string.Equals(Path.GetExtension(p), ".pth", StringComparison.OrdinalIgnoreCase))
            ?? validFiles.First();
        var packagePath = GetAvailablePackagePath(Path.GetFileNameWithoutExtension(primary));
        var assets = new List<(string Source, string Entry)>();
        foreach (var file in validFiles)
        {
            var entry = "model/" + SanitizeEntryFileName(Path.GetFileName(file));
            assets.Add((file, entry));
        }

        var hasPth = validFiles.Any(p => string.Equals(Path.GetExtension(p), ".pth", StringComparison.OrdinalIgnoreCase));
        var hasIndex = validFiles.Any(p => string.Equals(Path.GetExtension(p), ".index", StringComparison.OrdinalIgnoreCase));
        var hasOnnx = validFiles.Any(p => string.Equals(Path.GetExtension(p), ".onnx", StringComparison.OrdinalIgnoreCase));
        var engine = hasPth ? "RVC" : hasOnnx ? "ONNX" : "Unknown";
        var compatibility = hasPth
            ? (hasIndex ? "RVC candidate" : "RVC model · index optional")
            : hasOnnx ? "Conversion check required" : "Runtime check required";

        var preset = new AiVoicePreset
        {
            Name = Path.GetFileNameWithoutExtension(packagePath),
            VoiceType = "Imported",
            Engine = engine,
            Compatibility = compatibility,
            Source = "Local import",
            AssetEntries = assets.Select(a => a.Entry).ToList(),
            Notes = "Stored successfully. Runtime compatibility is verified when the AI backend is connected."
        };

        WritePackage(packagePath, preset, assets);
        preset.FilePath = packagePath;
        return preset;
    }

    public AiVoicePreset ImportPackage(string sourcePackagePath)
    {
        var source = LoadPackage(sourcePackagePath); // validates before copying
        var destination = GetAvailablePackagePath(source.Name);
        File.Copy(sourcePackagePath, destination, overwrite: false);
        return LoadPackage(destination);
    }

    public string Rename(AiVoicePreset preset, string newName)
    {
        if (preset.FilePath is null || !File.Exists(preset.FilePath))
        {
            throw new FileNotFoundException("The selected AI Voice package no longer exists.", preset.FilePath);
        }

        var cleanName = MakeSafeFileName(newName);
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            throw new InvalidDataException("Enter a valid voice name.");
        }

        var currentPath = Path.GetFullPath(preset.FilePath);
        var targetPath = Path.Combine(StorageDirectory, cleanName + VoiceExtension);
        if (string.Equals(currentPath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            preset.Name = cleanName;
            return currentPath;
        }

        if (File.Exists(targetPath))
        {
            throw new IOException($"An AI Voice named '{cleanName}' already exists.");
        }

        File.Move(currentPath, targetPath);
        preset.Name = cleanName;
        preset.FilePath = targetPath;
        return targetPath;
    }

    public void Delete(AiVoicePreset preset)
    {
        if (!string.IsNullOrWhiteSpace(preset.FilePath) && File.Exists(preset.FilePath))
        {
            File.Delete(preset.FilePath);
        }
    }

    public void Export(AiVoicePreset preset, string destinationPath)
    {
        if (preset.FilePath is null || !File.Exists(preset.FilePath))
        {
            throw new FileNotFoundException("The selected AI Voice package no longer exists.", preset.FilePath);
        }

        File.Copy(preset.FilePath, destinationPath, overwrite: true);
    }

    private static void WritePackage(
        string packagePath,
        AiVoicePreset preset,
        IEnumerable<(string Source, string Entry)> assets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifest = archive.CreateEntry(ManifestEntryName, CompressionLevel.Fastest);
        using (var stream = manifest.Open())
        {
            JsonSerializer.Serialize(stream, preset, JsonOptions);
        }

        foreach (var asset in assets)
        {
            var entry = archive.CreateEntry(asset.Entry, CompressionLevel.NoCompression);
            using var source = File.OpenRead(asset.Source);
            using var destination = entry.Open();
            source.CopyTo(destination);
        }
    }

    private string GetAvailablePackagePath(string requestedName)
    {
        var safeName = MakeSafeFileName(requestedName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "AI Voice";
        var candidate = Path.Combine(StorageDirectory, safeName + VoiceExtension);
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(StorageDirectory, $"{safeName} {suffix}{VoiceExtension}");
            suffix++;
        }
        return candidate;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim().TrimEnd('.');
    }

    private static string SanitizeEntryFileName(string value)
    {
        var name = MakeSafeFileName(value);
        return string.IsNullOrWhiteSpace(name) ? "asset.bin" : name;
    }
}
