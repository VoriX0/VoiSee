using System.Security.Cryptography;
using System.Text;
using VoiSe.Audio;

namespace VoiSe.App;

/// <summary>Versioned scene snapshots survive preset edits, deletion and renaming.</summary>
internal static class SceneVoiceState
{
    public static string LegacyPresetId(string path) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).ToLowerInvariant();

    public static VoicePreset? FindPreset(VoiSeScene scene, IReadOnlyList<VoicePreset> presets) =>
        !string.IsNullOrWhiteSpace(scene.VoicePresetId)
            ? presets.FirstOrDefault(p => p.Id == scene.VoicePresetId)
            : presets.FirstOrDefault(p => !string.IsNullOrWhiteSpace(scene.VoicePresetName) &&
                string.Equals(p.Name, scene.VoicePresetName, StringComparison.CurrentCultureIgnoreCase));

    public static void Capture(VoiSeScene scene, VoicePreset preset)
    {
        preset = VoicePresetSnapshot.Copy(preset);
        scene.VoicePresetId = preset.Id;
        scene.VoicePresetName = preset.Name;
        scene.VoiceSliders = new(preset.Sliders, StringComparer.OrdinalIgnoreCase);
        scene.VoiceEffectOrder = preset.EffectOrder.ToList();
        scene.VoiceBypassedEffects = preset.BypassedEffects.ToList();
        scene.VoiceChainBypassed = preset.ChainBypassed;
        scene.HasVoiceSnapshot = true;
        scene.SchemaVersion = Math.Max(10, scene.SchemaVersion);
    }
    public static VoicePreset? Resolve(VoiSeScene scene, IReadOnlyList<VoicePreset> presets)
    {
        if (!scene.HasVoiceSnapshot) return FindPreset(scene, presets);
        return new VoicePreset { SchemaVersion = 3, Id = scene.VoicePresetId ?? Guid.NewGuid().ToString("N"),
            Name = scene.VoicePresetName ?? scene.Name,
            Sliders = new(scene.VoiceSliders ?? new(), StringComparer.OrdinalIgnoreCase), EffectOrder = (scene.VoiceEffectOrder ?? new()).ToList(),
            BypassedEffects = (scene.VoiceBypassedEffects ?? new()).ToList(), ChainBypassed = scene.VoiceChainBypassed };
    }
    public static void Migrate(VoiSeScene scene, IReadOnlyList<VoicePreset> presets)
    {
        scene.VoiceEffectOrder ??= new();
        scene.VoiceBypassedEffects ??= new();
        if (scene.HasVoiceSnapshot) return;
        var preset = FindPreset(scene, presets);
        if (preset is not null) Capture(scene, preset);
        else
        {
            scene.VoiceEffectOrder = VoiceEffectCatalog.Ids.Where(id => scene.VoiceSliders.ContainsKey(id)).ToList();
            scene.HasVoiceSnapshot = true;
            scene.SchemaVersion = Math.Max(10, scene.SchemaVersion);
        }
    }
    public static void Clear(VoiSeScene scene)
    {
        scene.VoicePresetId = null; scene.VoicePresetName = null;
        scene.VoiceSliders.Clear(); scene.VoiceEffectOrder.Clear(); scene.HasVoiceSnapshot = true;
        scene.VoiceBypassedEffects = new(); scene.VoiceChainBypassed = false;
    }
}
