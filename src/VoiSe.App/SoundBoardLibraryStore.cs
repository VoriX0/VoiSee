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
        LibraryPath = Path.Combine(DataDirectory, "soundboard.json");
    }

    public string DataDirectory { get; }
    public string SoundsDirectory { get; }
    public string LibraryPath { get; }

    public SoundBoardLibrary Load()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(SoundsDirectory);

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
