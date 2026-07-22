# VoiSee 12.1.0 — Dual Noise Suppression

## Added

- Added `DeepFilterNet` as a second microphone noise-suppression engine.
- Added one compact engine selector with three modes:
  - Off
  - RNNoise
  - DeepFilterNet
- Kept one shared `Strength` control for both engines.
- Added automatic download and publish integration for the official DeepFilterNet 0.5.6 Windows x64 LADSPA library.

## Voice Changer layout

The Voice Changer page is now divided into three independent sections:

1. Noise suppression
2. Effects
3. Presets

Noise suppression is no longer shown as a special settings card and is visually separated from the effect controls. It remains global and is not stored inside voice presets.

## Removed

- Removed the experimental Low-frequency cleanup control.
- Removed its high-pass processor and persisted settings from the active model.
- Removed the separate RNNoise settings card.

## Audio routing

Only physical microphone audio is processed by RNNoise or DeepFilterNet. SoundBoard, scene sounds, and Media Bridge audio bypass microphone noise suppression.

The selected engine runs before gate, compressor, pitch, formant, tone controls, entertainment effects, and the final limiter.

## Build warning

Fixed:

```text
WINAPPSDKGENERATEPROJECTPRIFILE : warning : PRI249: 0xdef00520 - Invalid qualifier: NET-MIT
```

Third-party license texts are now ordinary copied files instead of PRI resources. Their filenames also use qualifier-safe underscore names.

## Compatibility

- Existing VoiSee 12.0 settings with enabled RNNoise are migrated to `RNNoise` mode.
- Noise suppression remains disabled for users who previously kept it off.
- Voice presets, scenes, SoundBoard, Media Bridge, and Discord screen-share isolation are unchanged.

## Validation status

The source archive was prepared without running a Windows build or automated tests.
