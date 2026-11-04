using VoiSe.Audio;

namespace VoiSe.App;

internal static class VoicePresetSnapshot
{
    public static VoicePreset Copy(VoicePreset source)
    {
        var values = source.Sliders ?? new();
        var modern = source.SchemaVersion >= 2 || source.EffectOrder is { Count: > 0 };
        var order = modern ? source.EffectOrder ?? new() :
            VoiceEffectCatalog.Ids.Where(id => values.TryGetValue(id, out var value) && LegacyActive(id, value)).ToList();
        var result = new VoicePreset { SchemaVersion = 3, Id = source.Id, Name = source.Name, Icon = source.Icon,
            PushToTalkHotkey = source.PushToTalkHotkey, PresetHotkey = source.PresetHotkey,
            ChainBypassed = source.ChainBypassed, FilePath = source.FilePath };
        foreach (var id in order)
        {
            if (id is null || !VoiceEffectCatalog.TryGet(id, out var effect) || result.EffectOrder.Contains(effect.Id)) continue;
            var value = values.TryGetValue(id, out var stored) && double.IsFinite(stored) ? stored : effect.DefaultValue;
            if (!modern && id is "Gate" or "Compressor") value += 100;
            result.EffectOrder.Add(effect.Id);
            result.Sliders[effect.Id] = Math.Clamp(value, effect.Minimum, effect.Maximum);
        }
        result.BypassedEffects = (source.BypassedEffects ?? new()).Where(id => result.EffectOrder.Contains(id, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return result;
    }
    private static bool LegacyActive(string id, double value) => double.IsFinite(value) && (id is "Gate" or "Compressor"
        ? value > -99.5 : VoiceEffectCatalog.Get(id).Minimum >= 0 ? value > .5 : Math.Abs(value) > .5);
}
