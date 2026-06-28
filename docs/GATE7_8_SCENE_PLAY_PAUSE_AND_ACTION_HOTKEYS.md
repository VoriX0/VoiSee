# Gate 7.8 — Scene play/pause and action hotkeys

## UI changes

- Increased Scene sound button size to make the per-button timeline and hotkey label fit.
- Centered the `+` add-sound button inside the same button shape as regular scene sounds.
- Removed the play/pause button from the looped sound timeline.
- Replaced looped sound `Play once` with a compact `Play / Stop` action.
- Kept the loop start action as a separate icon button.
- Added a Scene action hotkeys row:
  - Stop one-shots
  - Pause one-shots
  - Disable scene

## Playback behavior

- A scene can still have one looped background sound.
- Regular scene sounds play as independent single-instance overlays over the looped sound.
- Clicking a regular scene sound button:
  - starts it if it is not active;
  - pauses it if it is playing;
  - resumes it if it is paused.
- Multiple different regular scene sounds can still play at the same time.
- The same regular sound button cannot create multiple concurrent copies.

## Hotkey behavior

Priority remains:

1. Transport hotkeys
2. Scene hotkeys
3. SoundBoard hotkeys
4. Voice preset hotkeys

Scene hotkeys now include both per-button hotkeys and scene-level action hotkeys.

## SoundBoard lock

While a scene is active, SoundBoard playback controls, timeline seeking, and SoundBoard volume sliders remain locked. Scene playback keeps its own timelines and does not drive the SoundBoard timeline.
