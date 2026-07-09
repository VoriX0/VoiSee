# Gate 9.0 — Global Virtual Mic Mute

This release adds a global mute control for the virtual microphone route.

## Behavior

- Mute affects only the virtual microphone output.
- VoiSee monitor/headphones output continues to play.
- SoundBoard, scenes, looped sounds, and voice changer keep running.
- Volume sliders are not visually changed when mute is toggled.
- The audio engine applies mute as a final route-level gate for `AudioRoute.VirtualMicrophone`.
- A short cue sound is played only to the monitor/headphones route on mute/unmute.

## UI

- Header now shows `Mic Output: Live / Muted`.
- Header has a `Mute / Unmute` button.
- A red banner is displayed while the virtual mic route is muted.
- Settings hotkey dialog includes `Virtual Mic Mute`.

## Version

- Display version: `VoiSee Version 9.0`.
- Installer output: `VoiSee-Setup-9.0-x64.exe`.
- Portable output: `VoiSee-Portable-9.0-x64.zip`.
