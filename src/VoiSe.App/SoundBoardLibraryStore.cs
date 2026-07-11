using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoiSe.App;

public sealed class SoundBoardLibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] SupportedExtensions = [".wav", ".mp3", ".ogg"];

    public SoundBoardLibraryStore(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        SoundsDirectory = Path.Combine(DataDirectory, "sounds");
        EditedSoundsDirectory = Path.Combine(DataDirectory, "edited-sounds");
        LibraryPath = Path.Combine(DataDirectory, "soundboard.json");
    }

    public string DataDirectory { get; }
    public string SoundsDirectory { get; }
    public string EditedSoundsDirectory { get; }
    public string LibraryPath { get; }

    public SoundBoardLibrary Load()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(SoundsDirectory);
            Directory.CreateDirectory(EditedSoundsDirectory);

            if (!File.Exists(LibraryPath))
            {
                var fresh = CreateDefaultLibrary();
                Save(fresh);
                return fresh;
            }

            var json = File.ReadAllText(LibraryPath);
            var library = JsonSerializer.Deserialize<SoundBoardLibrary>(json, JsonOptions) ?? CreateDefaultLibrary();
            EnsureDefaultCategory(library);
            return library;
        }
        catch (Exception ex)
        {
            StartupLog.Write("SoundBoard library load error: " + ex);
            return CreateDefaultLibrary();
        }
    }

    public void Save(SoundBoardLibrary library)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(SoundsDirectory);
            Directory.CreateDirectory(EditedSoundsDirectory);
            EnsureDefaultCategory(library);
            var json = JsonSerializer.Serialize(library, JsonOptions);
            File.WriteAllText(LibraryPath, json);
        }
        catch (Exception ex)
        {
            StartupLog.Write("SoundBoard library save error: " + ex);
        }
    }

    public SoundBoardSound AddSound(SoundBoardLibrary library, string sourcePath, SoundBoardCategory category)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Sound file not found.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Unsupported sound format. Use WAV, MP3, or OGG.");
        }

        Directory.CreateDirectory(SoundsDirectory);
        var id = Guid.NewGuid().ToString("N");
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var safeName = MakeSafeFileName(sourceName);
        var targetFileName = $"{safeName}_{id[..8]}{extension}";
        var targetPath = Path.Combine(SoundsDirectory, targetFileName);
        File.Copy(sourcePath, targetPath, overwrite: false);

        var now = DateTime.UtcNow;
        var sound = new SoundBoardSound
        {
            Id = id,
            Name = sourceName,
            CategoryId = category.Id,
            FilePath = targetPath,
            OriginalFileName = Path.GetFileName(sourcePath),
            Extension = extension,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        library.Sounds.Add(sound);
        Save(library);
        return sound;
    }

    public SoundBoardCategory AddCategory(SoundBoardLibrary library, string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "New Category" : name.Trim();
        var category = new SoundBoardCategory
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmed,
            SortOrder = library.Categories.Count == 0 ? 0 : library.Categories.Max(c => c.SortOrder) + 1
        };
        library.Categories.Add(category);
        Save(library);
        return category;
    }

    public void DeleteSound(SoundBoardLibrary library, SoundBoardSound sound, bool deleteFile)
    {
        library.Sounds.RemoveAll(s => s.Id == sound.Id);
        Save(library);

        if (deleteFile && File.Exists(sound.FilePath))
        {
            try
            {
                File.Delete(sound.FilePath);
            }
            catch (Exception ex)
            {
                StartupLog.Write("Sound file delete error: " + ex);
            }
        }
    }

    public void RenameCategory(SoundBoardLibrary library, SoundBoardCategory category, string newName)
    {
        var trimmed = string.IsNullOrWhiteSpace(newName) ? category.Name : newName.Trim();
        category.Name = trimmed;
        Save(library);
    }

    public void DeleteCategory(SoundBoardLibrary library, SoundBoardCategory category, bool deleteFiles)
    {
        if (category.Id == "default" && library.Categories.Count == 1)
        {
            throw new InvalidOperationException("The last category cannot be deleted.");
        }

        var sounds = library.Sounds.Where(s => s.CategoryId == category.Id).ToList();
        foreach (var sound in sounds)
        {
            DeleteSound(library, sound, deleteFiles);
        }

        library.Categories.RemoveAll(c => c.Id == category.Id);
        EnsureDefaultCategory(library);
        Save(library);
    }

    public void RenameSound(SoundBoardLibrary library, SoundBoardSound sound, string newName)
    {
        sound.Name = string.IsNullOrWhiteSpace(newName) ? sound.DisplayName : newName.Trim();
        sound.UpdatedAtUtc = DateTime.UtcNow;
        Save(library);
    }

    public void SetHotkey(SoundBoardLibrary library, SoundBoardSound sound, string? hotkey)
    {
        sound.Hotkey = string.IsNullOrWhiteSpace(hotkey) ? null : hotkey.Trim();
        sound.UpdatedAtUtc = DateTime.UtcNow;
        Save(library);
    }

    public void ReplaceSoundFile(SoundBoardLibrary library, SoundBoardSound sound, string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Sound file not found.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Unsupported sound format. Use WAV, MP3, or OGG.");
        }

        Directory.CreateDirectory(SoundsDirectory);
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var safeName = MakeSafeFileName(sourceName);
        var targetFileName = $"{safeName}_{sound.Id[..8]}{extension}";
        var targetPath = Path.Combine(SoundsDirectory, targetFileName);

        if (!string.Equals(sound.FilePath, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(sound.FilePath))
        {
            try
            {
                File.Delete(sound.FilePath);
            }
            catch (Exception ex)
            {
                StartupLog.Write("Old sound file delete error: " + ex);
            }
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
        sound.FilePath = targetPath;
        sound.OriginalFileName = Path.GetFileName(sourcePath);
        sound.Extension = extension;
        sound.UpdatedAtUtc = DateTime.UtcNow;
        Save(library);
    }


    public void ReplaceSoundWithEditedWav(SoundBoardLibrary library, SoundBoardSound sound, string editedWavPath)
    {
        if (!File.Exists(editedWavPath))
        {
            throw new FileNotFoundException("Edited sound file not found.", editedWavPath);
        }

        Directory.CreateDirectory(SoundsDirectory);
        var safeName = MakeSafeFileName(sound.DisplayName);
        var targetFileName = $"{safeName}_{sound.Id[..8]}_edited.wav";
        var targetPath = Path.Combine(SoundsDirectory, targetFileName);
        var oldPath = sound.FilePath;

        if (Path.GetExtension(oldPath).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(oldPath), SoundsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            targetPath = oldPath;
        }

        var tempTarget = targetPath + ".tmp";
        File.Copy(editedWavPath, tempTarget, overwrite: true);

        if (File.Exists(targetPath))
        {
            var backupPath = targetPath + ".bak";
            try
            {
                File.Copy(targetPath, backupPath, overwrite: true);
            }
            catch (Exception ex)
            {
                StartupLog.Write("Sound backup error: " + ex);
            }

            File.Delete(targetPath);
        }

        File.Move(tempTarget, targetPath);

        if (!string.Equals(oldPath, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
        {
            try
            {
                File.Delete(oldPath);
            }
            catch (Exception ex)
            {
                StartupLog.Write("Old edited sound source delete error: " + ex);
            }
        }

        sound.FilePath = targetPath;
        sound.Extension = ".wav";
        sound.OriginalFileName = Path.GetFileName(targetPath);
        sound.UpdatedAtUtc = DateTime.UtcNow;
        Save(library);
    }

    public SoundBoardSound AddEditedCopy(SoundBoardLibrary library, SoundBoardSound sourceSound, string editedWavPath)
    {
        if (!File.Exists(editedWavPath))
        {
            throw new FileNotFoundException("Edited sound file not found.", editedWavPath);
        }

        Directory.CreateDirectory(SoundsDirectory);
        var id = Guid.NewGuid().ToString("N");
        var safeName = MakeSafeFileName(sourceSound.DisplayName + " copy");
        var targetFileName = $"{safeName}_{id[..8]}.wav";
        var targetPath = Path.Combine(SoundsDirectory, targetFileName);
        File.Copy(editedWavPath, targetPath, overwrite: false);

        var now = DateTime.UtcNow;
        var copy = new SoundBoardSound
        {
            Id = id,
            Name = sourceSound.DisplayName + " copy",
            CategoryId = sourceSound.CategoryId,
            FilePath = targetPath,
            OriginalFileName = Path.GetFileName(targetPath),
            Extension = ".wav",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        library.Sounds.Add(copy);
        Save(library);
        return copy;
    }

    public void IncrementUsage(SoundBoardLibrary library, SoundBoardSound sound)
    {
        sound.UsageCount++;
        var category = library.Categories.FirstOrDefault(c => c.Id == sound.CategoryId);
        if (category is not null)
        {
            category.UsageCount++;
        }

        Save(library);
    }

    private static SoundBoardLibrary CreateDefaultLibrary()
    {
        var library = new SoundBoardLibrary();
        EnsureDefaultCategory(library);
        return library;
    }

    private static void EnsureDefaultCategory(SoundBoardLibrary library)
    {
        if (library.Categories.Count == 0)
        {
            library.Categories.Add(new SoundBoardCategory
            {
                Id = "default",
                Name = "Default",
                SortOrder = 0
            });
        }
    }

    private static string MakeSafeFileName(string name)
    {
        var cleaned = Regex.Replace(name, "[^a-zA-Z0-9а-яА-Я._-]+", "_");
        return string.IsNullOrWhiteSpace(cleaned) ? "sound" : cleaned.Trim('_');
    }
}
