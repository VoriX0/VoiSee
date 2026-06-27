# Gate 7.2 — Scene editor active state and scene-local hotkeys

## Changes

- Scene list footer now contains only the main scene actions:
  - Apply
  - Rename
  - Delete
  - Create New
- The old top-level `Capture current scene` action was replaced with `Disable scenes`.
- Applying a scene marks it as the active scene in the scene list.
- Disabling scenes clears the active scene marker and stops the current SoundBoard transport.
- Scene settings were reorganized under a single `Scene Settings` header:
  - left side: one looped sound and the loop autostart checkbox;
  - right side: voice preset selection, Clear, Create new, and the synced Voice Monitor button.
- A scene now supports only one looped sound. Moving another sound to `Loop` automatically unloops the previous one.
- Scene sound hotkeys are now stored on the scene button (`SceneHotkey`) and no longer overwrite the source SoundBoard sound hotkey.
- Hotkey conflict priority is now:
  1. Transport hotkeys
  2. Active scene hotkeys
  3. SoundBoard sound hotkeys
  4. Voice preset hotkeys

## Compatibility

- Gate 7.0 / 7.1 scene JSON files are loaded and migrated.
- If an old scene has multiple looped sounds, only the first looped sound by sort order remains looped.
- Existing SoundBoard hotkeys remain unchanged.
