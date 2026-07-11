# Gate 9.2.2 — Centered Sound Editor and safe preview transport

Display version: `VoiSee Version 9.2.2`.

## Interface

- The Sound Editor dialog is explicitly centered horizontally and vertically.
- Preview controls are compact icon-only buttons placed above the waveform.
- Large Selection start/end/length cards were removed.
- Selection start is shown below the waveform on the left.
- Selection end is shown below the waveform on the right.
- Selected duration remains opposite Original duration above the waveform.
- A single click or drag inside the selected waveform positions the yellow playhead.
- The yellow playhead is rendered after every waveform and selection element, so it remains visually above the blue trim handles.

## Safe trim limits

- Minimum selected duration is `0.2 seconds` for sounds at least that long.
- For a source shorter than 0.2 seconds, the whole available duration is used as the minimum.
- Start and end handles cannot cross or collapse into the same position.

## Isolated editor session

Opening the editor now:

- stops current external SoundBoard/Scene playback;
- freezes the main SoundBoard timeline;
- disables normal VoiSee global hotkey actions;
- restores and clears an active push-to-talk voice preset state;
- redirects only the configured Play/Pause and Stop hotkeys to the editor preview.

## Play/Pause and Stop state machine

### While preview is playing

- Play/Pause pauses playback and freezes the playhead.
- Stop stops playback and returns the playhead to selection start.

### While preview is paused

- Play/Pause resumes from the same point.
- Stop stops playback and returns the playhead to selection start.

### While preview is stopped

- If the playhead is at selection start, both Play/Pause and Stop start preview using the current SoundBoard headphones volume.
- If the playhead is not at selection start, Play/Pause starts from the playhead.
- If the playhead is not at selection start, Stop only returns it to selection start.

### Natural completion

- Playback stops.
- The playhead returns to selection start.

## Preview modes

- Raw preview: full headphones volume.
- SoundBoard preview: current `SoundBoard → Headphones` volume.
- Both preview modes remain monitor-only and never route to the virtual microphone.
