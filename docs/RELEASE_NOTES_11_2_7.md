# VoiSee 11.2.7 — Audio Engine Process Isolation

## Purpose

This is a diagnostic architecture build for the reported Discord screen-share issue where the user's processed voice is heard twice when the VoiSee window is shared with application audio.

The previous 11.2.6 experiment moved only the final VB-CABLE renderer into a helper mode of `VoiSe.App.exe`. The duplicate remained. Version 11.2.7 instead gives the complete audio engine a different executable identity and removes all active audio streams from the WinUI process.

## New process layout

```text
VoiSe.App.exe
  WinUI, settings, scenes, profiles and controls
  no microphone capture
  no physical monitor output
  no VB-CABLE output
  no Media Bridge audio capture
          │
          │ private named pipe
          ▼
VoiSe.AudioHost.exe
  microphone capture
  Voice Changer DSP
  SoundBoard and scene audio
  Media Bridge process loopback
  physical headphone monitoring
  final mixer and limiter
  VB-CABLE output
```

`VoiSe.AudioHost.exe` is launched with Windows Explorer as its explicit parent. It therefore does not belong to the VoiSee UI process tree.

## Runtime behavior

- Starting the engine launches one hidden Audio Host process.
- Device IDs and effect settings are sent to the host over a private random named pipe.
- UI actions are translated into control commands for the host.
- Media Bridge meters and transport status are returned to the UI as lightweight snapshots.
- Closing or stopping VoiSee sends a shutdown command and terminates the host.
- If the UI crashes or the pipe disappears, the host exits after the connection closes.

## Preserved behavior

- Original hard Voice Monitor disconnect from 11.2.5.
- SoundBoard, scenes, editor preview and Media Bridge controls.
- Scene-owned versus manually owned Media Bridge behavior.
- Full-width Create / Rename / Delete category buttons.
- User data remains under `%LOCALAPPDATA%\VoiSe` and is not packed into the installer.

## Diagnostics

The application log shows:

```text
Engine started in isolated Audio Host PID <pid>.
```

The host writes its own startup and fatal-error log to:

```text
%LOCALAPPDATA%\VoiSe\audio-host.log
```

The expected executable beside the published application is:

```text
AudioHost\VoiSe.AudioHost.exe
```

## Build packaging

`scripts\build-installer.ps1` now publishes both executables as self-contained x64 applications. The installer and portable ZIP include the complete `AudioHost` subfolder.

## Verification status

No build, smoke test or automated test was run while preparing this archive, following the user's instruction. This version must be compiled and checked on Windows before it is treated as a stable release.
