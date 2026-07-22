# VoiSee 12.0.1 — Low-frequency cleanup

## Added

- A second global microphone cleanup stage: **Low-frequency cleanup**.
- Toggle and a single cutoff control from 50 to 160 Hz.
- Default cutoff is 90 Hz.
- The selected state and cutoff are persisted in user settings.

## Audio order

`Microphone → Low-frequency cleanup → RNNoise → Gate/Compressor → Voice effects → Limiter`

The filter is microphone-only. SoundBoard, scene sounds and Media Bridge are not processed.

## Compatibility

- Existing voice presets are unchanged.
- Existing settings files load with the new feature disabled by default.
- Discord screen-share isolation from 11.3.0 remains included.

## Verification

Build and automated tests were not run.
