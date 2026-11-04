namespace VoiSe.App;

/// <summary>Owns rack state; UI gestures edit it, audio and persistence receive defensive snapshots.</summary>
internal sealed class VoiceChainState
{
    public List<string> Order { get; } = new();
    public Dictionary<string, double> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> BypassedEffects { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Bypassed { get; set; }

    public VoicePreset Capture(string name, string icon = "\uE720") => new()
    {
        SchemaVersion = 3, Id = Guid.NewGuid().ToString("N"), Name = name, Icon = icon,
        EffectOrder = Order.ToList(), Sliders = new(Values, StringComparer.OrdinalIgnoreCase),
        BypassedEffects = BypassedEffects.ToList(), ChainBypassed = Bypassed
    };
    public void Restore(VoicePreset preset)
    {
        var snapshot = VoicePresetSnapshot.Copy(preset);
        Order.Clear(); Order.AddRange(snapshot.EffectOrder);
        Values.Clear(); foreach (var (id, value) in snapshot.Sliders) Values[id] = value;
        BypassedEffects.Clear(); BypassedEffects.UnionWith(snapshot.BypassedEffects);
        Bypassed = snapshot.ChainBypassed;
    }
}
