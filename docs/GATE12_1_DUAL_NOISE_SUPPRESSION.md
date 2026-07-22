# Gate 12.1 — Dual Noise Suppression Architecture

## Goal

Provide two interchangeable microphone-cleanup engines without coupling cleanup to the future draggable effect-chain model.

## Processing order

```text
Physical microphone
    -> selected noise suppression engine
       -> RNNoise
       -> or DeepFilterNet
       -> or bypass
    -> gate / compressor
    -> voice effects
    -> limiter
    -> virtual microphone and optional voice monitor
```

SoundBoard, scene sounds, and Media Bridge are mixed after the microphone processing path and therefore are not altered by either noise-suppression engine.

## UI model

The Voice Changer page has three top-level sections:

- Noise suppression: engine selector, Strength, and compact status text.
- Effects: current DSP sliders and Voice Monitor control.
- Presets: saved voice presets.

There are no dedicated settings cards for cleanup engines. Future effect panels can be redesigned independently without moving microphone cleanup into the preset chain.

## RNNoise

RNNoise retains the existing 480-sample, 48 kHz mono streaming adapter and aligned dry/wet strength blend.

## DeepFilterNet

VoiSee uses the official DeepFilterNet 0.5.6 Windows x64 LADSPA binary and its mono `deep_filter_mono` plug-in.

The C# adapter:

- dynamically loads `deep_filter_ladspa.dll`;
- resolves the standard `ladspa_descriptor` export;
- instantiates the mono processor at 48 kHz;
- downmixes the microphone bus to mono;
- processes the current callback block;
- duplicates the enhanced result to VoiSee's stereo microphone bus;
- maps the single user-facing Strength value to conservative DeepFilterNet controls.

The official LADSPA runtime owns a worker thread. VoiSee keeps one shared native runtime loaded for the lifetime of the application process so engine restarts do not repeatedly unload code while that worker is active.

## Native dependency delivery

`scripts/fetch-deepfilternet.ps1` downloads:

```text
deep_filter_ladspa-0.5.6-x86_64-pc-windows-msvc.dll
```

from the official DeepFilterNet GitHub release and stores it as:

```text
src/VoiSe.Audio/runtimes/win-x64/native/deep_filter_ladspa.dll
```

MSBuild copies it to normal build and publish output. The installer build therefore receives the same native binary automatically.

## Settings migration

`VoiSeUserSettings.NoiseSuppressionMode` stores the selected engine. The old `NoiseSuppressionEnabled` flag remains only for migration:

- old enabled setting + no mode -> RNNoise;
- old disabled setting + no mode -> Off.

The obsolete Low-frequency cleanup fields are no longer read or written.

## PRI249 fix

Third-party license files are declared as `None` with copy metadata rather than `Content`. This keeps them out of Windows PRI resource qualification. Qualifier-like filenames such as `RNNoise.NET-MIT.txt` were also renamed with underscores.

## Validation status

No Windows build or automated tests were run for this source package.
