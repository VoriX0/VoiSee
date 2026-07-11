# Gate 9.2.4 — Centered editor, compact toolbar, and selection-safe playback

Display version: `VoiSee Version 9.2.4`.

## UI changes

- The Sound Editor now recalculates its actual position after layout and translates itself to the center of the application XamlRoot.
- Volume gain was moved above the waveform, to the right of the editor toolbar.
- The two playback buttons were swapped:
  1. play the selected fragment;
  2. play the full edited sound from the beginning.
- The selected-fragment playback icon is now `[▷]`.
- `Trim Outside` now uses `⛶`.
- `Cut Selection` now uses `✀`.

## Selection and playhead behavior

Creating or changing a selection no longer changes the yellow playhead position.

- If preview is stopped, the playhead stays where it was while the user drags a selection.
- If full-sound preview is active, it continues to the end captured when playback began.
- If selection preview is active, it continues to the previous selection end captured when playback began, even if the user creates a different selection during playback.
- A single click still intentionally moves the playhead.
- Dragging the yellow playhead intentionally stops the current preview and repositions it.

The preview-selection command now plays only from the selection start to the selection end rather than continuing to the end of the whole file.
