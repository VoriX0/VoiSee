# VoiSe Gate 6.0 — Voice Changer Presets

Gate 6 starts the Voice Changer product layer on top of the working SoundBoard foundation from Gate 5.36.

## Run

```powershell
dotnet run --project src/VoiSe.App
```

## What is new

- Window title/header updated to Gate 6.0.
- The Voice Changer tab now has a dedicated design:
  - title `Voice processing`;
  - `Voice Monitor` button in the header area;
  - normalized `-100..+100` sliders;
  - 3 sliders per row;
  - preset tiles below the sliders;
  - last tile is always `New preset` with a large `+`.
- New voice presets are saved as separate JSON files for easy exchange:

```powershell
%LOCALAPPDATA%\VoiSe\presets\<preset-name>.json
```

## Current voice sliders

- Input Gain
- Voice Gain
- Pitch
- Formant
- Gate
- Compressor
- Compression Ratio
- Limiter
- Robot
- Radio
- Reverb
- Brightness

Gate 6.0 applies the supported DSP controls live to the running engine: input gain, voice gain, gate, compressor, compression ratio and limiter. The other sliders are already stored in presets and reserved for the next DSP passes.
