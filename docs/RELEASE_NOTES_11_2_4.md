# VoiSee 11.2.4

## Voice Monitor routing experiment

The hard monitor-route disconnect introduced in 11.2.3 has been removed.

Voice Monitor now uses the original gain-based behaviour:

- **Off** sets processed microphone monitor gain to `0%`;
- **On** restores it to `100%`;
- the monitor output session remains active;
- the processed microphone continues to be sent to the virtual microphone independently.

This release is intended to test whether gain-only monitoring changes the duplicated voice heard when sharing the VoiSee window with application audio. It does not claim that the Discord screen-share duplication is conclusively fixed.

## SoundBoard category controls

The category action row now has three equal columns instead of six unused columns. `Create`, `Rename`, and `Delete` again stretch across the full width of the category panel.

## Unchanged

- Media Bridge and its scene integration;
- SoundBoard audio routing;
- Voice Changer processing;
- virtual microphone output;
- scene playback behaviour.

## Verification

No build, automated test, or smoke test was run for this archive.
