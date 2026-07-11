# GATE 9.2 Buildfix 2 — Timeline sound editor redesign

## Goal
Replace the rough MVP slider-based sound editor with a cleaner timeline-oriented editor closer to classic audio editors.

## Changes
- Sound Editor now shows a waveform/timeline area instead of visible trim sliders.
- The selected fragment is edited directly on the waveform:
  - drag the left handle to move selection start;
  - drag the right handle to move selection end;
  - drag inside the highlighted range to move the entire selection.
- The highlighted region is the fragment that will be saved.
- Added selection metrics:
  - selection start;
  - selection end;
  - selection length.
- Preview button now uses an icon-based button (`Play`) and previews only the selected fragment in headphones.
- Added icon buttons for `Stop` and `Reset`.
- Gain remains available as a dedicated control below the waveform.

## Notes
- Save File overwrites the current library sound with the rendered WAV result.
- Save as Copy creates a new library item based on the selected fragment.
- Preview continues to use the dedicated `SoundEditorPreviewPlaybackKey` so it does not interfere with normal SoundBoard or Scene playback.
