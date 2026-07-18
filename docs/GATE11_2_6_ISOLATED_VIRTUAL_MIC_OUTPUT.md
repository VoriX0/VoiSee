# Gate 11.2.6 — Isolated Virtual Microphone Output

## Problem

During application-window screen sharing with audio, the remote listener receives the processed voice twice:

1. through Discord's selected VB-CABLE microphone input;
2. through the shared VoiSee application audio.

The second copy follows Voice Changer processing, which suggests that it may be the VoiSee virtual-output render stream rather than the physical monitor route.

## Decision

Move only the final VB-CABLE render session to a detached helper process. Keep capture, DSP, mixing and UI ownership in the main VoiSee process.

## Components

### `VirtualMicOutputHost`

A hidden command-line mode in the existing `VoiSe.App.exe` executable. It:

- bypasses normal WinUI startup and single-instance coordination;
- connects to a named pipe;
- reads the negotiated 48 kHz stereo IEEE-float format and VB-CABLE device ID;
- feeds a `BufferedWaveProvider`;
- owns the only virtual-output `WasapiOut` instance.

### `IsolatedVirtualMicOutput`

A replacement for the in-process virtual `WasapiOut`. It:

- creates a per-run, current-user-only named pipe;
- starts the helper outside the VoiSee UI process tree;
- pulls 20 ms blocks from the existing `RouteMixSampleProvider`;
- sends framed float PCM blocks to the helper;
- closes and terminates the helper with the engine.

### `DetachedProcessLauncher`

Uses `STARTUPINFOEX` and `PROC_THREAD_ATTRIBUTE_PARENT_PROCESS` to create the helper with the current-session `explorer.exe` process as its parent.

No normal child-process fallback is used. A fallback would make the diagnostic result ambiguous because process-loopback capture can include child processes.

## Unchanged components

- microphone `WasapiCapture`;
- `SimpleVoiceProcessor`;
- `RouteMixSampleProvider` mixing rules;
- SoundBoard transport;
- Media Bridge capture and transport;
- physical monitor `WasapiOut`;
- Voice Monitor hard disconnect;
- scene ownership rules.

## Expected diagnostic result

When Discord shares the VoiSee window with application audio:

- the UI process may still expose physical monitor and SoundBoard monitor audio;
- the VB-CABLE render session is owned by a process outside the UI process tree;
- the processed microphone should therefore arrive only through Discord's microphone input, not through VoiSee application audio.

If doubling remains, the cause is not limited to capturing the VoiSee UI process tree's VB-CABLE render session. Possible next areas would include executable-wide grouping by the screen-sharing application, endpoint-level loopback, or a separate Discord routing behavior.

## Validation status

No build, automated test or smoke test was run in this environment by explicit user request.
