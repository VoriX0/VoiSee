# Gate 7.10 buildfix 4 — Restore Voice Changer after disabling scenes

Final Gate 7 scene lifecycle fix.

## Behavior

- When a scene is applied and no scene was active before it, the app captures the current Voice Changer slider state.
- Scene application may then apply the scene voice preset or scene slider snapshot.
- When scenes are disabled, the captured pre-scene Voice Changer slider state is restored.
- The previous applied voice preset marker is restored as metadata, but restoration is slider-based so custom unsaved voice settings are preserved too.
- Switching from one active scene to another does not overwrite the original pre-scene snapshot.
- Deleting the active scene also restores the pre-scene Voice Changer state.

This keeps scene voice settings temporary and makes Gate 7 ready for Gate 8.
