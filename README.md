# VoiSe Gate 6.9 — Voice and Settings Scroll Fix

Gate 6.9 keeps the working Gate 6.8 / Gate 6.5 SoundBoard wheel behavior and extends the same low-level wheel-routing approach to:

- the Voice Changer tab, from the tab content top down to the bottom of the window;
- the Settings log area, from the log area top down to the bottom of the window.

The important rule: do **not** replace the working SoundBoard wheel calibration with the centered/client-pixel zone from Gate 6.6/6.7, because that breaks SoundBoard scrolling in fullscreen.

## Run

```powershell
dotnet run --project src/VoiSe.App
```

## Active voice sliders

- Voice Gain
- Gate
- Compressor
- Pitch
- Bass
- Treble
- Distortion
- Robot
- Tremolo
- Echo
- Reverb
- Radio
- Bit Crusher
- Alien

## Pitch behavior

- Negative values lower the voice toward a deeper/bassier sound.
- Positive values raise the voice toward a thinner/squeakier sound.
- Slider range remains `-100..+100` and maps roughly to `-12..+12` semitones.
- Numeric fields may exceed the slider range, but the DSP path clamps pitch to a safe `-24..+24` semitones.

## Scroll logic

- SoundBoard: kept exactly on the restored working Gate 6.8 logic.
- Voice Changer: wheel below the tab headers routes to `VoiceChangerScrollViewer` down to the end of the window.
- Settings: wheel inside/below the log area routes to the internal log `ScrollViewer`.

## Presets

New and recreated presets save all active Gate 6.9 sliders as separate JSON files in:

```powershell
%LOCALAPPDATA%\VoiSe\presets\
```

Existing older presets still load; removed keys are ignored.
