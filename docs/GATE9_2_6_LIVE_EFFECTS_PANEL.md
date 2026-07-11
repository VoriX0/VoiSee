# Gate 9.2.6 — Live effects panel

Display version: `VoiSee Version 9.2.6`.

## Goal
Add a non-destructive effects section below the Sound Editor timeline and show the influence of every effect on the waveform immediately.

## Effects included
- Volume gain: -24 dB to +12 dB.
- Normalize: raises the source peak to approximately -0.18 dBFS before the user gain stage.
- Fade in: 0 seconds up to the current edited duration.
- Fade out: 0 seconds up to the current edited duration.
- Distortion: 0–100% soft-clip drive.

## Behaviour
- The waveform uses the current effect values during every slider move or toggle change.
- Preview renders the same effect chain that is displayed on the waveform.
- Previewing only a selection preserves fade timing relative to the entire edited sound rather than restarting the fade at the selection boundary.
- `Save File` and `Save as` render the current effect chain.
- Trim Outside and Cut Selection remain destructive timeline operations, while effects remain non-destructive until save.
- Reset restores the original source and clears all effects.

## Effect order
1. Normalize.
2. Volume gain.
3. Fade envelope.
4. Soft-clip distortion.
