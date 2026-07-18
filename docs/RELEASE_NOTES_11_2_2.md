# VoiSee 11.2.2

## Scene Media Source simplification

- Removed the Media Source volume control from the Scenes tab.
- Removed `Stop Broadcast` from the scene editor.
- Moved `Launch` into the media source card, immediately to the left of the selected window name.
- Scene playback now uses only the selected Media Bridge profile and whether capture should start with the scene.
- Media Bridge volume is always taken from the main Media Bridge tab.
- Pause, Stop, meters, and future audio processing remain exclusively on the Media Bridge tab.
- A scene-owned broadcast still stops when its scene is disabled or replaced.
- A manually running Media Bridge broadcast is still never replaced or stopped by a scene.

## Compatibility

The legacy `MediaBridgeVirtualMicVolume` field remains readable in existing scene files for backward compatibility, but it is no longer exposed or used by scene playback.

## Verification

Per project instruction, no build, automated tests, smoke tests, or XAML compiler run was performed.
