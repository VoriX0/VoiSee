# VoiSee 12.3.0 buildfix 4 — exact column wheel zones and displaced-card animation

## Wheel routing

Voice Changer no longer calibrates wheel zones from internal panels or percentage offsets.

Three transparent XAML borders are placed in the same `VoiceChangerStudioRoot` grid columns as the visible UI:

- `VoicePresetWheelColumnZone` — column 0;
- `ProcessingChainWheelColumnZone` — column 1;
- `EffectLibraryWheelColumnZone` — column 2.

They use the exact same `Grid.ColumnDefinitions`, therefore their widths track the visible columns automatically. Negative vertical margins cancel the workspace top/bottom padding so the zones span the full Voice Changer content height. Because the workspace itself begins below the TabView header, wheel routing is clipped above at the tab-content boundary.

The low-level wheel router now tests only these three zones. The temporary 30%/50% expansion and shift constants from buildfix 3 were removed.

## Chain animation

Before add/reorder, VoiSee captures the actual Y position of every current effect card. After rebuilding the ordered chain, it calculates `oldY - newY` for every surviving card and animates a `TranslateTransform` from that offset to zero. This is a FLIP-style transition: cards displaced by a newly inserted effect visibly slide to their new positions instead of teleporting.

The newly inserted card still receives the existing short entrance animation.

## Functional scope

No DSP ordering, preset schema, manual value range, noise-suppression behavior, or effect algorithms were changed.
