# Gate 6.0 — Voice Changer Presets

## Goal

Turn the Voice Changer tab from a small technical slider set into a usable preset-based UI.

## UI Contract

- Voice Changer begins with the `Voice processing` title.
- The `Voice Monitor` button is placed on the same header line, on the right.
- Sliders use a normalized `-100..+100` scale.
- Sliders are arranged 3 per row.
- Presets are shown as square tiles with an icon and a label.
- The final tile is always `New preset` with a `+`.

## Preset Storage

Each user preset is a standalone JSON file:

```text
%LOCALAPPDATA%\VoiSe\presets\<preset-name>.json
```

This is intentional: presets can later be exchanged by sending JSON files.

## Gate 6.0 Limitation

The UI already stores all sliders in presets. The current audio engine applies the controls it supports now:

- Input Gain
- Voice Gain
- Gate
- Compressor
- Compression Ratio
- Limiter

Pitch, Formant, Robot, Radio, Reverb and Brightness are prepared in UI/presets for later DSP implementation.
