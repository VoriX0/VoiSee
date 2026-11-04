using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VoiSe.App;

public sealed class VoicePresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private const string DefaultPresetJson = """
{
  "SchemaVersion": 2,
  "Name": "Default",
  "Icon": "\uE720",
  "Sliders": {},
  "EffectOrder": []
}
""";

    public VoicePresetStore(string dataDirectory)
    {
        PresetsDirectory = Path.Combine(dataDirectory, "presets");
        Directory.CreateDirectory(PresetsDirectory);
    }

    public string PresetsDirectory { get; }

    public IReadOnlyList<VoicePreset> LoadPresets()
    {
        EnsureDefaultPresetWhenEmpty();

        var presets = new List<VoicePreset>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(PresetsDirectory, "*.json").OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                var preset = AtomicFile.ReadJson<VoicePreset>(file, JsonOptions);
                if (preset is null || string.IsNullOrWhiteSpace(preset.Name))
                {
                    continue;
                }

                preset.Icon = string.IsNullOrWhiteSpace(preset.Icon) ? "\uE720" : preset.Icon;
                preset.Sliders ??= new Dictionary<string, double>();
                preset.EffectOrder ??= new List<string>();
                if (string.IsNullOrWhiteSpace(preset.Id) || !ids.Add(preset.Id)) preset.Id = SceneVoiceState.LegacyPresetId(file);
                ids.Add(preset.Id);
                preset.FilePath = file;
                presets.Add(VoicePresetSnapshot.Copy(preset));
            }
            catch
            {
                // A broken exchanged preset should not break application startup.
            }
        }

        return presets
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public string SavePreset(VoicePreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Id)) preset.Id = string.IsNullOrWhiteSpace(preset.FilePath) ? Guid.NewGuid().ToString("N") : SceneVoiceState.LegacyPresetId(preset.FilePath);
        Directory.CreateDirectory(PresetsDirectory);
        var path = GetAvailablePresetPath(preset.Name);
        preset.FilePath = path;
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(VoicePresetSnapshot.Copy(preset), JsonOptions));
        return path;
    }

    public string ImportPreset(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException(UiText.L("Preset_JSON_file_not_found_490b00cf"), sourcePath);
        }

        var json = File.ReadAllText(sourcePath);
        var preset = JsonSerializer.Deserialize<VoicePreset>(json, JsonOptions)
            ?? throw new InvalidDataException(UiText.L("Selected_JSON_is_not_a_VoiSe_voice_preset_6358c054"));

        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            preset.Name = Path.GetFileNameWithoutExtension(sourcePath);
        }

        if (string.IsNullOrWhiteSpace(preset.Id) || LoadPresets().Any(p => p.Id == preset.Id)) preset.Id = Guid.NewGuid().ToString("N");
        preset.SchemaVersion = Math.Max(1, preset.SchemaVersion);
        preset.Icon = string.IsNullOrWhiteSpace(preset.Icon) ? "\uE720" : preset.Icon;
        preset.Sliders ??= new Dictionary<string, double>();
        preset.EffectOrder ??= new List<string>();
        preset.FilePath = GetAvailablePresetPath(preset.Name);
        AtomicFile.WriteAllText(preset.FilePath, JsonSerializer.Serialize(VoicePresetSnapshot.Copy(preset), JsonOptions));
        return preset.FilePath;
    }

    public string OverwritePreset(VoicePreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Id)) preset.Id = string.IsNullOrWhiteSpace(preset.FilePath) ? Guid.NewGuid().ToString("N") : SceneVoiceState.LegacyPresetId(preset.FilePath);
        Directory.CreateDirectory(PresetsDirectory);
        var path = !string.IsNullOrWhiteSpace(preset.FilePath)
            ? preset.FilePath!
            : Path.Combine(PresetsDirectory, MakeSafeFileName(preset.Name) + ".json");

        preset.FilePath = path;
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(VoicePresetSnapshot.Copy(preset), JsonOptions));
        return path;
    }

    public string RenamePreset(VoicePreset preset, string newName)
    {
        Directory.CreateDirectory(PresetsDirectory);
        var oldPath = preset.FilePath;
        var previousName = preset.Name;
        if (string.IsNullOrWhiteSpace(preset.Id)) preset.Id = oldPath is null ? Guid.NewGuid().ToString("N") : SceneVoiceState.LegacyPresetId(oldPath);
        preset.Name = newName.Trim();
        var newPath = GetAvailablePresetPath(preset.Name, oldPath);
        try { AtomicFile.RenameContent(oldPath, newPath, JsonSerializer.Serialize(VoicePresetSnapshot.Copy(preset), JsonOptions)); }
        catch { preset.Name = previousName; throw; }
        preset.FilePath = newPath;
        return newPath;
    }

    public void DeletePreset(VoicePreset preset)
    {
        if (!string.IsNullOrWhiteSpace(preset.FilePath) && File.Exists(preset.FilePath))
        {
            File.Delete(preset.FilePath);
        }
    }

    private void EnsureDefaultPresetWhenEmpty()
    {
        Directory.CreateDirectory(PresetsDirectory);
        if (Directory.EnumerateFiles(PresetsDirectory, "*.json").Any())
        {
            return;
        }

        AtomicFile.WriteAllText(Path.Combine(PresetsDirectory, "Default.json"), DefaultPresetJson);
    }

    private string GetAvailablePresetPath(string presetName, string? currentPath = null)
    {
        var safeName = MakeSafeFileName(presetName);
        var path = Path.Combine(PresetsDirectory, safeName + ".json");
        if (!string.IsNullOrWhiteSpace(currentPath) && string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var suffix = 2;
        while (File.Exists(path) && !string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(PresetsDirectory, safeName + "-" + suffix + ".json");
            suffix++;
        }

        return path;
    }

    private static string MakeSafeFileName(string name)
    {
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Preset" : safe;
    }
}
