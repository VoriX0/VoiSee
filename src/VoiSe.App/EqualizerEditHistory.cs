using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VoiSe.Audio;

namespace VoiSe.App;

// UI-only, bounded snapshots. A gesture retains its starting state once,
// regardless of how many audio updates are applied while the pointer moves.
internal sealed class EqualizerEditHistory
{
    internal sealed record State(EqualizerSettings Settings, int SelectedBand);
    private readonly List<State> _undo = new();
    private State _current;
    private bool _gesture, _gestureRecorded;
    private string? _mergeKey;
    private long _lastEdit;
    internal const int Capacity = 100;
    internal int Count => _undo.Count;

    internal EqualizerEditHistory(EqualizerSettings initial, int selectedBand) =>
        _current = new(initial.NormalizedCopy(), selectedBand);

    internal void SelectBand(int index, EqualizerSettings settings)
    {
        if (Same(_current.Settings, settings)) _current = _current with { SelectedBand = index };
    }
    internal void BeginGesture()
    {
        EndGesture();
        _gesture = true;
    }
    internal void EndGesture()
    {
        if (_gestureRecorded && _undo.Count > 0 && Same(_undo[^1].Settings, _current.Settings))
            _undo.RemoveAt(_undo.Count - 1);
        _gesture = _gestureRecorded = false;
        _mergeKey = null;
    }
    internal void Record(EqualizerSettings settings, int selectedBand, string? mergeKey = null)
    {
        if (Same(_current.Settings, settings)) return;
        var now = Stopwatch.GetTimestamp();
        var merge = _gesture ? _gestureRecorded : mergeKey is not null && mergeKey == _mergeKey
            && Stopwatch.GetElapsedTime(_lastEdit, now).TotalMilliseconds < 600;
        if (!merge)
        {
            if (_undo.Count == Capacity) _undo.RemoveAt(0);
            _undo.Add(_current);
        }
        _current = new(settings.NormalizedCopy(), selectedBand);
        _gestureRecorded = _gesture;
        _mergeKey = mergeKey;
        _lastEdit = now;
    }
    internal State? Undo()
    {
        EndGesture();
        if (_undo.Count == 0) return null;
        _current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        // The caller may mutate the restored model; history must own its copy.
        return new(_current.Settings.NormalizedCopy(), _current.SelectedBand);
    }
    private static bool Same(EqualizerSettings a, EqualizerSettings b) =>
        a.Enabled == b.Enabled && a.PreampDb == b.PreampDb
        && a.GainsDb.SequenceEqual(b.GainsDb) && a.ToneGainsDb.SequenceEqual(b.ToneGainsDb)
        && a.ParametricBands.SequenceEqual(b.ParametricBands);
}
