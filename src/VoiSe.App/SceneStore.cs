using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoiSe.App;

public sealed class SceneStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SceneStore(string dataDirectory)
    {
        ScenesDirectory = Path.Combine(dataDirectory, "scenes");
        Directory.CreateDirectory(ScenesDirectory);
    }

    public string ScenesDirectory { get; }

    public IReadOnlyList<VoiSeScene> LoadScenes()
    {
        Directory.CreateDirectory(ScenesDirectory);
        var scenes = new List<VoiSeScene>();

        foreach (var file in Directory.EnumerateFiles(ScenesDirectory, "*.json").OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                var json = File.ReadAllText(file);
                var scene = JsonSerializer.Deserialize<VoiSeScene>(json, JsonOptions);
                if (scene is null || string.IsNullOrWhiteSpace(scene.Name))
                {
                    continue;
                }

                scene.Id = string.IsNullOrWhiteSpace(scene.Id) ? Guid.NewGuid().ToString("N") : scene.Id;
                scene.SchemaVersion = Math.Max(1, scene.SchemaVersion);
                scene.Icon = string.IsNullOrWhiteSpace(scene.Icon) ? "🎬" : scene.Icon;
                scene.VoiceSliders ??= new Dictionary<string, double>();
                scene.OneShotSoundIds ??= new List<string>();
                scene.SoundButtons ??= new List<SceneSoundButton>();
                MigrateLegacySceneSounds(scene);
                EnforceSingleLoopedSound(scene);
                NormalizeSceneSoundVolumes(scene);
                scene.FilePath = file;
                scenes.Add(scene);
            }
            catch
            {
                // A broken shared scene should not break application startup.
            }
        }

        return scenes
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public string SaveScene(VoiSeScene scene)
    {
        Directory.CreateDirectory(ScenesDirectory);
        PrepareSceneForSave(scene);
        scene.FilePath = GetAvailableScenePath(scene.Name);
        File.WriteAllText(scene.FilePath, JsonSerializer.Serialize(scene, JsonOptions));
        return scene.FilePath;
    }

    public string OverwriteScene(VoiSeScene scene)
    {
        Directory.CreateDirectory(ScenesDirectory);
        PrepareSceneForSave(scene);

        var path = !string.IsNullOrWhiteSpace(scene.FilePath)
            ? scene.FilePath!
            : Path.Combine(ScenesDirectory, MakeSafeFileName(scene.Name) + ".json");

        scene.FilePath = path;
        File.WriteAllText(path, JsonSerializer.Serialize(scene, JsonOptions));
        return path;
    }

    public string RenameScene(VoiSeScene scene, string newName)
    {
        Directory.CreateDirectory(ScenesDirectory);
        var oldPath = scene.FilePath;
        scene.Name = string.IsNullOrWhiteSpace(newName) ? scene.Name : newName.Trim();
        scene.UpdatedAtUtc = DateTime.UtcNow;
        var newPath = GetAvailableScenePath(scene.Name, oldPath);

        if (!string.IsNullOrWhiteSpace(oldPath) && File.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(oldPath, newPath);
        }

        scene.FilePath = newPath;
        File.WriteAllText(newPath, JsonSerializer.Serialize(scene, JsonOptions));
        return newPath;
    }

    public void DeleteScene(VoiSeScene scene)
    {
        if (!string.IsNullOrWhiteSpace(scene.FilePath) && File.Exists(scene.FilePath))
        {
            File.Delete(scene.FilePath);
        }
    }

    private static void PrepareSceneForSave(VoiSeScene scene)
    {
        scene.SchemaVersion = Math.Max(6, scene.SchemaVersion);
        scene.Id = string.IsNullOrWhiteSpace(scene.Id) ? Guid.NewGuid().ToString("N") : scene.Id;
        scene.Icon = string.IsNullOrWhiteSpace(scene.Icon) ? "🎬" : scene.Icon;
        scene.VoiceSliders ??= new Dictionary<string, double>();
        scene.OneShotSoundIds ??= new List<string>();
        scene.SoundButtons ??= new List<SceneSoundButton>();
        MigrateLegacySceneSounds(scene);
        EnforceSingleLoopedSound(scene);
        NormalizeSceneSoundVolumes(scene);
        scene.CreatedAtUtc = scene.CreatedAtUtc == default ? DateTime.UtcNow : scene.CreatedAtUtc;
        scene.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void MigrateLegacySceneSounds(VoiSeScene scene)
    {
        if (scene.SoundButtons.Count != 0)
        {
            return;
        }

        var sortOrder = 0;
        if (!string.IsNullOrWhiteSpace(scene.BackgroundSoundId))
        {
            scene.SoundButtons.Add(new SceneSoundButton
            {
                SoundId = scene.BackgroundSoundId!,
                LocalName = scene.BackgroundSoundName,
                VirtualMicVolume = scene.SceneButtonsVirtualMicVolume,
                HeadphonesVolume = scene.SceneButtonsHeadphonesVolume,
                IsLooped = true,
                SortOrder = sortOrder++
            });
        }

        foreach (var soundId in scene.OneShotSoundIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            if (scene.SoundButtons.Any(button => button.SoundId == soundId))
            {
                continue;
            }

            scene.SoundButtons.Add(new SceneSoundButton
            {
                SoundId = soundId,
                VirtualMicVolume = scene.SceneButtonsVirtualMicVolume,
                HeadphonesVolume = scene.SceneButtonsHeadphonesVolume,
                IsLooped = false,
                SortOrder = sortOrder++
            });
        }
    }

    private static void NormalizeSceneSoundVolumes(VoiSeScene scene)
    {
        scene.LoopedSoundHeadphonesVolume = NormalizeVolume(scene.LoopedSoundHeadphonesVolume);
        scene.LoopedSoundVirtualMicVolume = NormalizeVolume(scene.LoopedSoundVirtualMicVolume);
        scene.SceneButtonsHeadphonesVolume = NormalizeVolume(scene.SceneButtonsHeadphonesVolume);
        scene.SceneButtonsVirtualMicVolume = NormalizeVolume(scene.SceneButtonsVirtualMicVolume);

        foreach (var button in scene.SoundButtons)
        {
            button.HeadphonesVolume = NormalizeVolume(button.HeadphonesVolume);
            button.VirtualMicVolume = NormalizeVolume(button.VirtualMicVolume);
        }
    }

    private static double NormalizeVolume(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.0;
        }

        return Math.Max(0.0, Math.Min(1.5, value));
    }


    private static void EnforceSingleLoopedSound(VoiSeScene scene)
    {
        var firstLoop = scene.SoundButtons
            .Where(button => button.IsLooped)
            .OrderBy(button => button.SortOrder)
            .FirstOrDefault();

        if (firstLoop is null)
        {
            return;
        }

        foreach (var button in scene.SoundButtons)
        {
            button.IsLooped = button.Id == firstLoop.Id;
        }
    }

    private string GetAvailableScenePath(string sceneName, string? currentPath = null)
    {
        var safeName = MakeSafeFileName(sceneName);
        var path = Path.Combine(ScenesDirectory, safeName + ".json");
        if (!string.IsNullOrWhiteSpace(currentPath) && string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var suffix = 2;
        while (File.Exists(path) && !string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(ScenesDirectory, safeName + "-" + suffix + ".json");
            suffix++;
        }

        return path;
    }

    private static string MakeSafeFileName(string name)
    {
        var cleaned = Regex.Replace(name, "[^a-zA-Z0-9а-яА-Я._-]+", "_");
        return string.IsNullOrWhiteSpace(cleaned) ? "Scene" : cleaned.Trim('_');
    }
}
