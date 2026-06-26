# VoiSe Gate 6.6 — Centered SoundBoard Wheel Zone

Gate 6.6 keeps the Gate 6.5 Voice Changer / Pitch work and changes only the SoundBoard wheel catch-zone calibration.

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

## Removed from Gate 6.4

- Timbre was removed because it sounded too similar to Bass.
- Chorus was removed because the effect was not noticeable enough.
- Alien was kept.

## SoundBoard wheel zone

The SoundBoard wheel catch-zone was expanded again:

- +60% to the right;
- +50% downward.

## Presets

New and recreated presets save all active Gate 6.6 sliders as separate JSON files in:

```powershell
%LOCALAPPDATA%\VoiSe\presets\
```

Existing older presets still load; removed keys are ignored.


## Gate 6.6 change

The SoundBoard wheel catch-zone is now centered on the whole application window and clipped so it starts below the tab selector. When the SoundBoard tab is active, scrolling anywhere below the tabs should scroll only the Sounds list.
