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

    public VoicePresetStore(string dataDirectory)
    {
        PresetsDirectory = Path.Combine(dataDirectory, "presets");
        Directory.CreateDirectory(PresetsDirectory);
    }

    public string PresetsDirectory { get; }

    public IReadOnlyList<VoicePreset> LoadPresets()
    {
        EnsureStarterPresets();

        var presets = new List<VoicePreset>();
        foreach (var file in Directory.EnumerateFiles(PresetsDirectory, "*.json").OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                var json = File.ReadAllText(file);
                var preset = JsonSerializer.Deserialize<VoicePreset>(json, JsonOptions);
                if (preset is null || string.IsNullOrWhiteSpace(preset.Name))
                {
                    continue;
                }

                preset.Sliders ??= new Dictionary<string, double>();
                presets.Add(preset);
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
        Directory.CreateDirectory(PresetsDirectory);
        var safeName = MakeSafeFileName(preset.Name);
        var path = Path.Combine(PresetsDirectory, safeName + ".json");
        var suffix = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(PresetsDirectory, safeName + "-" + suffix + ".json");
            suffix++;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(preset, JsonOptions));
        return path;
    }

    private void EnsureStarterPresets()
    {
        Directory.CreateDirectory(PresetsDirectory);
        if (Directory.EnumerateFiles(PresetsDirectory, "*.json").Any())
        {
            return;
        }

        var starterPresets = new[]
        {
            new VoicePreset { Name = "Normal", Sliders = SliderMap(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) },
            new VoicePreset { Name = "Deep Voice", Sliders = SliderMap(0, 10, -45, -25, 5, -5, 10, 0, 0, 0, 0, -10) },
            new VoicePreset { Name = "High Voice", Sliders = SliderMap(0, 0, 42, 24, 0, 0, 0, 0, 0, 0, 0, 18) },
            new VoicePreset { Name = "Robot", Sliders = SliderMap(0, 0, 0, -10, 5, 12, 18, 0, 85, 10, 0, 0) },
            new VoicePreset { Name = "Demon", Sliders = SliderMap(0, 18, -60, -40, 8, 12, 12, 0, 20, 0, 40, -20) },
            new VoicePreset { Name = "Radio", Sliders = SliderMap(0, 6, 0, 0, 20, 25, 25, -8, 0, 80, 0, 35) }
        };

        foreach (var preset in starterPresets)
        {
            var path = Path.Combine(PresetsDirectory, MakeSafeFileName(preset.Name) + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(preset, JsonOptions));
        }
    }

    private static Dictionary<string, double> SliderMap(
        double inputGain,
        double voiceGain,
        double pitch,
        double formant,
        double gate,
        double compressor,
        double compressionRatio,
        double limiter,
        double robot,
        double radio,
        double reverb,
        double brightness)
    {
        return new Dictionary<string, double>
        {
            ["InputGain"] = inputGain,
            ["VoiceGain"] = voiceGain,
            ["Pitch"] = pitch,
            ["Formant"] = formant,
            ["Gate"] = gate,
            ["Compressor"] = compressor,
            ["CompressionRatio"] = compressionRatio,
            ["Limiter"] = limiter,
            ["Robot"] = robot,
            ["Radio"] = radio,
            ["Reverb"] = reverb,
            ["Brightness"] = brightness
        };
    }

    private static string MakeSafeFileName(string name)
    {
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Preset" : safe;
    }
}
