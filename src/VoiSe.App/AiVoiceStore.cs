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
        NormalizeCompatibility(preset);
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
            Notes = "Reference audio stored. Use Quick Clone or Optimize Voice from the Selected Voice panel."
        };

        WritePackage(packagePath, preset, new[] { (sourceAudioPath, audioEntry) });
        preset.FilePath = packagePath;
        return preset;
    }

    public AiVoicePreset CreateImportedModel(
        IReadOnlyList<string> modelFiles,
        string? requestedName = null,
        string source = "Local import",
        string? notes = null)
    {
        var validFiles = modelFiles.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (validFiles.Count == 0)
        {
            throw new InvalidDataException("No model files were selected.");
        }

        var primary = validFiles.FirstOrDefault(p => string.Equals(Path.GetExtension(p), ".pth", StringComparison.OrdinalIgnoreCase))
            ?? validFiles.First();
        var packagePath = GetAvailablePackagePath(string.IsNullOrWhiteSpace(requestedName)
            ? Path.GetFileNameWithoutExtension(primary)
            : requestedName!);
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
            ? (hasIndex ? "RVC activation candidate" : "RVC activation candidate · index optional")
            : hasOnnx ? "Not supported · conversion required" : "Not supported";

        var preset = new AiVoicePreset
        {
            Name = Path.GetFileNameWithoutExtension(packagePath),
            VoiceType = "Imported",
            Engine = engine,
            Compatibility = compatibility,
            Source = string.IsNullOrWhiteSpace(source) ? "Local import" : source,
            AssetEntries = assets.Select(a => a.Entry).ToList(),
            Notes = string.IsNullOrWhiteSpace(notes)
                ? "Stored successfully. Runtime compatibility is verified when the AI backend is connected."
                : notes
        };

        WritePackage(packagePath, preset, assets);
        preset.FilePath = packagePath;
        return preset;
    }

    public AiVoicePreset PrepareQuickClone(AiVoicePreset preset)
    {
        if (preset.FilePath is null || !File.Exists(preset.FilePath))
        {
            throw new FileNotFoundException("The selected AI Voice package no longer exists.", preset.FilePath);
        }

        if (!preset.CanQuickClone || string.IsNullOrWhiteSpace(preset.ReferenceAudioEntry))
        {
            throw new InvalidOperationException("Quick Clone can only be prepared from a Reference voice.");
        }

        var packagePath = Path.GetFullPath(preset.FilePath);
        var durationSeconds = MeasureReferenceDurationSeconds(packagePath, preset.ReferenceAudioEntry);
        if (durationSeconds < 10.0)
        {
            throw new InvalidDataException($"Reference audio is {durationSeconds:0.0} seconds long. Quick Clone requires at least 10 seconds of usable reference audio.");
        }

        preset.SchemaVersion = Math.Max(2, preset.SchemaVersion);
        preset.VoiceType = "Quick Clone";
        preset.Engine = "Quick Clone";
        preset.Compatibility = "Quick Clone ready · runtime pending";
        preset.ReferenceDurationSeconds = durationSeconds;
        preset.QuickPreparedUtc = DateTimeOffset.UtcNow;
        preset.QuickProfileEntry = "quick/profile.json";
        if (!preset.AssetEntries.Contains(preset.QuickProfileEntry, StringComparer.OrdinalIgnoreCase))
        {
            preset.AssetEntries.Add(preset.QuickProfileEntry);
        }
        preset.Notes = "Quick Clone profile prepared from the stored reference. Realtime AI inference is connected in a later runtime Gate.";

        RewritePackageWithQuickProfile(packagePath, preset);
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

    private static void NormalizeCompatibility(AiVoicePreset preset)
    {
        var assets = preset.AssetEntries ?? new List<string>();
        var hasPth = assets.Any(entry => entry.EndsWith(".pth", StringComparison.OrdinalIgnoreCase));
        var hasIndex = assets.Any(entry => entry.EndsWith(".index", StringComparison.OrdinalIgnoreCase));
        var hasOnnx = assets.Any(entry => entry.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(preset.VoiceType, "Quick Clone", StringComparison.OrdinalIgnoreCase))
        {
            preset.Engine = "Quick Clone";
            preset.Compatibility = "Quick Clone ready · runtime pending";
            return;
        }

        if (string.Equals(preset.VoiceType, "Optimized", StringComparison.OrdinalIgnoreCase))
        {
            preset.Engine = string.IsNullOrWhiteSpace(preset.Engine) ? "Optimized" : preset.Engine;
            preset.Compatibility = "Optimized voice · runtime pending";
            return;
        }

        if (hasPth)
        {
            preset.Engine = "RVC";
            preset.Compatibility = hasIndex
                ? "RVC activation candidate"
                : "RVC activation candidate · index optional";
            return;
        }

        if (hasOnnx)
        {
            preset.Engine = "ONNX";
            preset.Compatibility = "Not supported · conversion required";
            return;
        }

        if (string.Equals(preset.VoiceType, "Reference", StringComparison.OrdinalIgnoreCase))
        {
            preset.Engine = "Pending";
            preset.Compatibility = "Ready for Quick Clone";
            return;
        }

        preset.Compatibility = "Not supported";
    }

    private static double MeasureReferenceDurationSeconds(string packagePath, string referenceEntryName)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "VoiSee", "QuickClone", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var referenceEntry = archive.GetEntry(referenceEntryName)
                ?? throw new InvalidDataException("The .vsee package no longer contains its reference audio.");
            var extension = Path.GetExtension(referenceEntry.Name);
            var tempPath = Path.Combine(tempDirectory, "reference" + extension);
            referenceEntry.ExtractToFile(tempPath, overwrite: true);

            if (string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new NAudio.Vorbis.VorbisWaveReader(tempPath);
                return reader.TotalTime.TotalSeconds;
            }

            using var audio = new NAudio.Wave.AudioFileReader(tempPath);
            return audio.TotalTime.TotalSeconds;
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); } catch { }
        }
    }

    private static void RewritePackageWithQuickProfile(string packagePath, AiVoicePreset preset)
    {
        var tempPackage = packagePath + ".quick.tmp";
        if (File.Exists(tempPackage)) File.Delete(tempPackage);

        using (var source = ZipFile.OpenRead(packagePath))
        using (var destination = ZipFile.Open(tempPackage, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                if (string.Equals(entry.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.FullName, "quick/profile.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var copy = destination.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                using var input = entry.Open();
                using var output = copy.Open();
                input.CopyTo(output);
            }

            var quickProfile = destination.CreateEntry("quick/profile.json", CompressionLevel.Fastest);
            using (var stream = quickProfile.Open())
            {
                var profile = new
                {
                    schema = 1,
                    kind = "zero-shot-reference",
                    referenceEntry = preset.ReferenceAudioEntry,
                    referenceDurationSeconds = preset.ReferenceDurationSeconds,
                    preparedUtc = preset.QuickPreparedUtc
                };
                JsonSerializer.Serialize(stream, profile, JsonOptions);
            }

            var manifest = destination.CreateEntry(ManifestEntryName, CompressionLevel.Fastest);
            using var manifestStream = manifest.Open();
            JsonSerializer.Serialize(manifestStream, preset, JsonOptions);
        }

        File.Move(tempPackage, packagePath, overwrite: true);
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
