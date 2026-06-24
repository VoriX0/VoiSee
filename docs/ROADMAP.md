# Roadmap

## Gate 0 — Audio Prototype

- device listing
- mic -> VB-CABLE passthrough
- monitoring
- simple gate/compressor/limiter
- latency check

## Gate 1 — Data Model

- JSON config
- sound/category/scene/preset models
- schemaVersion

## Gate 2 — WinUI 3 shell

- tabs: Voice Changer, Scenes, SoundBoard, Settings
- device status
- basic settings screen

## Gate 3 — MVP features

- SoundBoard list/categories
- scenes
- three presets
- hotkeys
- tray mode


## Gate 1 completed in this package

- Add one-shot sound playback into virtual output and monitoring output.
- Add OGG/Vorbis decoding dependency through NAudio.Vorbis.
- Add runtime keys: S to play, X to stop.

Next: internal mix bus, volume separation, Stop all, and first WinUI 3 shell.
