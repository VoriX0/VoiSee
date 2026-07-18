# Gate 11.2.2 — Scene Media controls simplification

## Goal

Keep Scenes focused on selecting and launching a Media Bridge source. Keep all live audio controls on the Media Bridge tab.

## Changes

### Scenes tab

Removed from the `Media Source` editor:

- scene-specific virtual-microphone volume;
- `Stop Broadcast`.

Moved `Launch` into the selected source card, immediately to the left of the source/window title.

The scene editor now contains:

- background type selector;
- Media Bridge profile selector;
- source card with `Launch` and the source/window title;
- `Capture source when scene starts`.

### Playback ownership

When a scene starts Media Bridge, capture uses the current Media Bridge tab volume and settings.

A scene does not overwrite:

- Media Bridge volume;
- Pause state;
- meter behavior;
- future normalization or ducking settings.

A scene-owned capture still stops when that scene is disabled or replaced. A manually running capture remains protected from scene shutdown.

### Compatibility

The old scene volume field remains in the data model only for compatibility with existing scene JSON. It is no longer presented in the UI or consumed by scene playback.

## Verification

No build, automated test, smoke test, or XAML compiler run was performed, per project instruction.
