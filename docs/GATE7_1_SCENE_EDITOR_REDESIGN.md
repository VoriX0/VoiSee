# Gate 7.1 — Scene Editor Redesign

Gate 7.1 changes the Scenes tab from a read-only snapshot view into a scene editor.

## Implemented

- The left scene list and scene management buttons remain in place.
- The selected scene editor now has a top Voice Changer preset row:
  - existing preset picker;
  - `Clear` button to remove the preset from the scene;
  - `Create new` button that switches to the Voice Changer tab.
- Scenes persist a new `SoundButtons` list with per-scene sound button data:
  - source `SoundId` from SoundBoard;
  - scene-local `LocalName` alias;
  - `IsLooped` placement flag;
  - `SortOrder`.
- Looped sounds are displayed in a separate list above normal scene sound buttons.
- The looped header has the `Start looped sounds when scene is enabled` checkbox at the right edge.
- Normal scene sound buttons end with a rectangular `+` button. It has the same shape as sound buttons.
- Pressing `+` opens a flyout with:
  - category selector;
  - search box;
  - filtered sound list from the existing SoundBoard library.
- Each scene sound button is a rectangular button with the same dimensions as the `+` button.
- Right-clicking a scene sound button opens a context menu with:
  - source SoundBoard sound name at the top;
  - scene-only rename;
  - choose another existing SoundBoard sound;
  - delete;
  - SoundBoard hotkey editing;
  - loop/unloop movement between the normal and looped lists.
- Hotkey editing from a scene updates the source SoundBoard sound hotkey, not a scene-local copy.
- Gate 7.0 scene JSON is migrated into the new Gate 7.1 `SoundButtons` model when loaded.

## Notes

The audio transport still has the existing single SoundBoard transport. When loop autostart is enabled, applying a scene starts the first looped sound through the current transport and logs that true multi-loop playback belongs to the future loop layer.
