# Gate 7.4 — Scene loop slot polish and overlay sounds

## UI changes

- The scene control button group in the left scene menu is moved higher by reducing the scene list height.
- The scene controls remain arranged as:
  - Apply + Disable
  - Delete + Rename
  - Create New spanning both columns
- The looped sound area is no longer rendered as a clickable Button.
- The looped sound slot is rendered as a sound-style pill/card:
  - selected sound: local scene name or SoundBoard name;
  - no selected sound: `No sound` with the same visual shape.
- The looped sound action buttons are the same height as the looped sound slot.
- The looped sound choose/replace action uses the `⮏` icon.
- Voice Monitor is now in the same horizontal row as Clear and Create new.

## Playback changes

- SoundBoard transport now supports one loop/primary layer plus one-shot overlay sounds.
- When a looped sound is already running, playing a normal scene sound starts it as an overlay instead of replacing the loop.
- Transport Stop still stops all SoundBoard layers.
- Transport Pause/Resume applies to all active SoundBoard layers.

## Notes

- Voice Monitor remains global and synchronized with Voice Changer.
- Applying a scene does not toggle Voice Monitor.
