# VoiSe Gate 6.2 — Voice Active Sliders

Gate 6.2 keeps the Gate 6 preset workflow, but removes Voice Changer sliders that are not wired to the current DSP yet.

## Run

```powershell
dotnet run --project src/VoiSe.App
```

## Changes

- Window title/version updated to **VoiSe Gate 6.2**.
- SoundBoard wheel catch zone expanded:
  - +40% to the right compared with Gate 6.1 tuning;
  - +35% downward compared with Gate 6.1 tuning.
- Voice Changer work area has extra right padding so the vertical scrollbar does not cover controls.
- Sounds list content also has extra right padding so its scrollbar does not cover rows.
- Voice Changer now shows only active sliders:
  - `Voice Gain`;
  - `Gate`;
  - `Compressor`.
- Removed inactive current UI sliders:
  - `Input Gain`;
  - `Pitch`;
  - `Formant`;
  - `Compression Ratio`;
  - `Limiter`;
  - `Robot`;
  - `Radio`;
  - `Reverb`;
  - `Brightness`.
- Preset capture/recreate now writes only active slider values.
- Fresh `Default.json` in `%LOCALAPPDATA%\VoiSe\presets\` now contains only active sliders.
- Compressor slider is mapped more aggressively: one slider now controls both compression threshold and internal ratio.

## Preset folder

```powershell
%LOCALAPPDATA%\VoiSe\presets\
```

A fresh preset folder starts with only:

```text
Default.json
```

Existing exchanged presets with old keys still load; unsupported keys are ignored by the Gate 6.2 UI.
