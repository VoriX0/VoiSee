# VoiSee 11.2.6 — Isolated Virtual Mic Output

## Purpose

This is a diagnostic architecture change for the reported case where sharing the VoiSee application window with audio produces a second copy of the processed microphone voice.

The working hypothesis is that the screen-sharing application captures VoiSee's VB-CABLE render stream as application audio. VoiSee 11.2.6 removes that render session from the UI process.

## Changed audio topology

Before:

```text
VoiSee UI process
  microphone capture
  voice processing
  SoundBoard / Media Bridge mix
  VB-CABLE WasapiOut
  headphone monitor WasapiOut
```

VoiSee 11.2.6:

```text
VoiSee UI process
  microphone capture
  voice processing
  SoundBoard / Media Bridge mix
  headphone monitor WasapiOut
            |
            | named pipe: final 48 kHz stereo float mix
            v
Detached VoiSee helper process
  VB-CABLE WasapiOut only
```

## Process-tree isolation

The helper uses the same `VoiSe.App.exe` binary with a hidden `--virtual-mic-host` mode. It is created with Windows Explorer as its explicit parent process instead of the VoiSee UI process.

This is deliberate: application loopback capture may include the selected process and its child processes. Keeping the helper outside the VoiSee UI process tree prevents a process-tree capture of the VoiSee window from automatically including the VB-CABLE render stream.

## Runtime behavior

- The helper is started automatically when the audio engine starts.
- No additional visible window or console is created.
- The UI sends the already mixed virtual-microphone stream through a per-run named pipe.
- The helper opens the selected VB-CABLE render endpoint and outputs the stream.
- Stopping the engine closes the pipe and terminates the helper.
- If detached startup or pipe connection fails, engine startup fails instead of silently falling back to the old in-process render path.

## Diagnostics

The main VoiSee log should contain:

```text
Virtual output isolation: detached host PID <number>.
```

The helper writes lifecycle and fatal errors to:

```text
%LOCALAPPDATA%\VoiSe\virtual-mic-host.log
```

## Preserved behavior

- Voice Monitor keeps the restored hard route-disconnect behavior from 11.2.5.
- SoundBoard, scenes and Media Bridge continue to feed the same final virtual-microphone mix.
- The physical headphone monitor remains owned by the UI process.
- The full-width Create / Rename / Delete category-button layout is preserved.

## Important status

This build is an experimental diagnostic branch. It has not been compiled or tested in this environment, following the user's instruction not to run builds or tests.
