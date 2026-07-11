# Gate 9.2 — Sound Editor MVP

Display version: `VoiSee Version 9.2`.

## Added

- SoundBoard track editor.
- Right click a sound or select it and press `Edit Track`.
- Trim start/end.
- Gain adjustment in dB.
- Preview to headphones only through the monitor route.
- `Save File`: updates the selected SoundBoard item and keeps scenes/hotkeys tied to the same sound id.
- `Save as Copy`: creates a duplicated SoundBoard item named `<name> copy`.
- Edited output is rendered as WAV for reliable local playback.
- Audio cache and duration cache are invalidated after editing.

## Notes

- Source MP3/OGG/WAV files are decoded through the existing audio stack.
- Preview requires the VoiSee audio engine to be running, because it is routed only to headphones/monitor and not to the virtual microphone.
- Save still works when preview is unavailable.
