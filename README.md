# VoiSe Gate 6.1 — Voice Preset Management

Gate 6.1 focuses on the Voice Changer tab.

## Changes

- Initial presets folder now starts with only one `Default.json` preset when the folder is empty.
- Voice preset tiles now have a right-click menu:
  - Select
  - Rename
  - Recreate from current sliders
  - Choose hotkey
  - Delete
- Hotkey dialog stores two fields in preset JSON:
  - Push to talk
  - Preset select
- Each voice slider now has a numeric value box above it.
- Sliders stay limited to `-100..+100`.
- Numeric boxes allow experimental values from `-9999..+9999`.
- Moving a slider updates its value box.
- Typing a value updates the slider position, clamped visually to `-100..+100`.
- Voice changes are debounced to reduce UI lag while dragging sliders.

## Presets folder

```powershell
%LOCALAPPDATA%\VoiSe\presets\
```

## Run

```powershell
dotnet run --project src/VoiSe.App
```

## DSP note

Gate 6.1 improves UI and preset management. The current audio processor is still simple and only has obvious live effect for gain, gate and compression/limiting. Higher quality pitch/formant/robot/radio/reverb needs a dedicated DSP layer in the next Gate.
