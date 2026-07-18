# VoiSee 11.2 — Scene Media Background

## Implemented

- Scene background type: Looped Sound or Media Source.
- Media profiles are created from windows selected on the Media Bridge tab.
- Media mode hides the HP control and exposes a dedicated Mic volume.
- Launch Source opens the saved desktop executable or a known browser fallback.
- Stop Broadcast calls the same stop operation as the Media Bridge tab.
- Capture source when scene starts resolves the saved profile only when the scene is explicitly applied.
- A scene-owned broadcast stops when that scene is disabled or replaced.
- A manually running Media Bridge broadcast is not replaced and is not stopped when a scene ends.
- Existing scenes remain Looped Sound by default.

## Deferred

- Normalization and simplified Ducking processing.
- Profile management beyond automatic creation/update from selected windows.
- Provider-specific APIs.
