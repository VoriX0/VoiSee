using System.Collections.ObjectModel;

namespace VoiSe.Audio;

public sealed record VoiceEffectDescriptor(string Id, VoiceEffectKind Kind, double Minimum, double Maximum, double DefaultValue);

/// <summary>Stable effect IDs and UI ranges shared by persisted snapshots and the rack.</summary>
public static class VoiceEffectCatalog
{
    public static IReadOnlyList<VoiceEffectDescriptor> All { get; } = Array.AsReadOnly(
        Enum.GetValues<VoiceEffectKind>().Select(kind =>
        {
            var minimum = kind is VoiceEffectKind.VoiceGain or VoiceEffectKind.Pitch or VoiceEffectKind.Formant
                or VoiceEffectKind.Bass or VoiceEffectKind.Treble ? -100d : 0d;
            return new VoiceEffectDescriptor(kind.ToString(), kind, minimum, kind == VoiceEffectKind.Compressor ? 200 : 100, 0);
        }).ToArray());
    private static readonly IReadOnlyDictionary<string, VoiceEffectDescriptor> ById =
        new ReadOnlyDictionary<string, VoiceEffectDescriptor>(All.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase));
    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(All.Select(e => e.Id).ToArray());
    public static bool TryGet(string id, out VoiceEffectDescriptor descriptor) => ById.TryGetValue(id, out descriptor!);
    public static VoiceEffectDescriptor Get(string id) => ById[id];
}
