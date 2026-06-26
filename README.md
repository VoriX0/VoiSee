# VoiSe Gate 6.10 — Extended Voice and Settings Scroll

Gate 6.10 keeps the working Gate 6.8 / Gate 6.5 SoundBoard wheel behavior and fixes the important difference that caused Voice Changer and Settings to stop scrolling near the bottom of a fullscreen window: SoundBoard used an extended bottom wheel zone, while Voice Changer and Settings were clipped at `RootGrid.ActualHeight`.


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

- SoundBoard: kept on the restored working Gate 6.8 logic.
- Voice Changer: now uses the same extended-bottom rule as SoundBoard, routing wheel events from the Voice Changer content top through the lower fullscreen area to `VoiceChangerScrollViewer`.
- Settings: now uses the same extended-bottom rule from the log area top through the lower fullscreen area to the internal log `ScrollViewer`.

## Presets

New and recreated presets save all active Gate 6.10 sliders as separate JSON files in:

```powershell
%LOCALAPPDATA%\VoiSe\presets\
```

Existing older presets still load; removed keys are ignored.
