# Gate 7.3 — Scene loop slot layout and monitor behavior

## Scope

Gate 7.3 refines the Gate 7 scene editor UI and playback behavior.

## Scene list actions

The scene list action area is now arranged as a balanced button grid:

- Row 1: `Apply` and `Disable`.
- Row 2: `Delete` and `Rename`.
- Row 3: `Create New`, spanning the full two-button width.

`Disable` replaces the earlier top-level disable button location and keeps the same behavior: it clears the active scene marker and stops current SoundBoard transport.

## Looped sound slot

The looped sound area is now a single scene slot instead of a framed list.

- The outer border around the looped sound was removed.
- The looped sound button stretches across the available slot width.
- Four icon-only actions sit to the right of the looped sound slot:
  - `↻` — start the selected looped sound in loop mode.
  - `▶` — play the selected looped sound once.
  - `✕` — remove the selected looped sound from the scene.
  - `…` — choose or replace the looped sound from existing SoundBoard sounds.
- Tooltips provide text labels for these icon buttons.
- The `Loop` / `Unloop` item was removed from the right-click menu on scene sound buttons.

## Loop playback

The SoundBoard transport now supports loop playback for scene looped sounds. Normal SoundBoard playback remains one-shot by default.

When `Start looped sound when scene is enabled` is checked, applying the scene starts the looped sound in loop mode.

## Voice monitor behavior

Voice Monitor remains synchronized between the Voice Changer and Scene editor buttons, but it is no longer stored/applied as part of a scene. Applying a scene will not turn monitoring on or off.
