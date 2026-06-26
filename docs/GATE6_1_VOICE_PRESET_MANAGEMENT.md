# Gate 6.1 — Voice Preset Management

This gate extends the Voice Changer tab.

## Implemented

- Empty `%LOCALAPPDATA%\VoiSe\presets` is initialized with one preset: `Default.json`.
- Presets are individual JSON files for easy sharing.
- Preset context menu:
  - Select
  - Rename
  - Recreate
  - Choose hotkey
  - Delete
- Hotkey JSON fields:
  - `PushToTalkHotkey`
  - `PresetHotkey`
- Slider value text boxes:
  - Slider range remains `-100..+100`.
  - Manual text input supports `-9999..+9999`.
  - Slider and text box sync both ways.
- Voice setting application is debounced to reduce lag while dragging sliders.

## DSP status

Current DSP is still basic. It applies gain, gate, compressor threshold, compression ratio and limiter. Pitch/formant/robot/radio/reverb are stored in presets but need a dedicated DSP implementation in a later gate.
