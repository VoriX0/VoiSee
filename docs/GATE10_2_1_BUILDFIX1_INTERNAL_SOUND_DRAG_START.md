# VoiSee 10.2.1 buildfix 1 — internal SoundBoard drag start

## Symptom

The full-window Explorer import overlay worked, but dragging an existing SoundBoard sound toward the category ComboBox did not start an internal drag operation.

## Fix

The SoundBoard input overlay now uses explicit mouse gesture detection instead of relying on the implicit `CanDrag` gesture:

- a valid left press stores the exact sound, pointer ID and press position;
- movement of at least 8 pixels starts `SoundInputOverlay.StartDragAsync(pointerPoint)`;
- `StartDragAsync` raises the existing `DragStarting` handler, which supplies the sound ID and Move/Copy operations;
- release, cancellation, failed start and completed drop reset gesture state;
- right-click context menu, single-click selection and double-click playback remain on the same overlay;
- external Explorer import remains separate from internal sound dragging.

The application version remains 10.2.1; this archive is buildfix 1 for that gate.

## Windows runtime checks

1. Press a sound row and move the mouse at least a few pixels.
2. Move over the category ComboBox and wait about 420 ms for it to open.
3. Drop on another category to Move.
4. Hold Ctrl during drop to Copy.
5. Verify single-click selection, double-click playback, context menu, wheel scrolling and Explorer import.
