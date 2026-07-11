# GATE 9.2 Buildfix 5 — Dialog width, draggable playhead, and preview isolation

## Fixed
- Sound Editor now overrides WinUI ContentDialog width resources so the dialog is actually wider, instead of only giving its child controls a larger width.
- Waveform, selection metrics, action buttons, and duration labels are sized to fit inside the widened dialog.
- `Selected duration` is initialized immediately and displayed opposite `Original duration`.
- The yellow preview playhead is wider, has a visible top cap, and can be dragged with the mouse.
- The time ruler supports click-and-drag scrubbing of the preview start position.
- Main SoundBoard transport timeline is frozen while Sound Editor preview is active and is restored when preview stops.

## Interaction
- Drag blue boundary handles to change the saved selection.
- Drag the yellow playhead to choose the preview start position.
- Drag anywhere on the top time ruler to scrub the yellow playhead.
- Preview starts from the yellow playhead and ends at the selection end.
