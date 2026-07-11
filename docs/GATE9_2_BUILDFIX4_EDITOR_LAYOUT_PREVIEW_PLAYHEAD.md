# GATE 9.2 Buildfix 4 — Sound Editor layout, preview modes, and playhead

## Goal
Polish the Sound Editor after the first timeline implementation.

## Changes
- Increased the editor content width so the waveform and metrics fit without overflowing the dialog.
- Reworked the layout so the timeline, selection metrics, buttons, and labels stay inside the editor window.
- Added a second preview mode:
  - `Preview Raw` — headphones preview at full volume.
  - `Preview Board Vol` — headphones preview using the current SoundBoard headphones volume.
- Added a vertical playhead line on the waveform during preview playback.
- Added preview position scrubbing:
  - click the top time ruler to set the preview start position;
  - double-click the waveform to set the preview start position.
- Preview now starts from the current playhead position and plays until the end of the selected fragment.
- Added `Selected duration` text opposite `Original duration` above the waveform.
- Kept `Stop` and `Reset` actions.

## Notes
- Preview still routes only to headphones and never to the virtual microphone.
- Save File and Save as Copy still render only the selected fragment.
