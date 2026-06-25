using System;
using System.IO;
using System.Text.Json;

namespace VoiSe.App;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsStore()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiSe");
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
    }

    public string DataDirectory { get; }
    public string SettingsPath { get; }

    public VoiSeUserSettings Load()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            if (!File.Exists(SettingsPath))
            {
                return new VoiSeUserSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<VoiSeUserSettings>(json, JsonOptions);
            return settings ?? new VoiSeUserSettings();
        }
        catch (Exception ex)
        {
            StartupLog.Write("Settings load error: " + ex);
            return new VoiSeUserSettings();
        }
    }

    public void Save(VoiSeUserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            StartupLog.Write("Settings save error: " + ex);
        }
    }
}
