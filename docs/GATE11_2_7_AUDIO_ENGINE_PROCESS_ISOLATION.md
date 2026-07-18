# Gate 11.2.7 — Full Audio Engine Process Isolation

## Decision

The VoiSee UI process must not own any active capture or render audio session. A separate executable, `VoiSe.AudioHost.exe`, owns the full `Gate2UnifiedAudioEngine` instance.

## Why the 11.2.6 experiment was insufficient

11.2.6 moved only the final virtual-output renderer and launched another instance of `VoiSe.App.exe` in a hidden command-line mode. The UI process still owned microphone capture, DSP, SoundBoard mixing, Media Bridge and physical monitoring. The helper also had the same executable identity as the shared application.

11.2.7 removes both ambiguities:

1. every active audio stream is moved out of the UI process;
2. the host has a separate executable file and process identity.

## Components

### VoiSe.App

- `AudioEngineClient.cs` mirrors the public operations used by `MainWindow`;
- `DetachedAudioHostLauncher.cs` creates the host with Explorer as parent;
- a private bidirectional named pipe carries JSON command envelopes;
- a cached snapshot supplies Media Bridge peaks and route state;
- the UI still enumerates devices for selection, but opens no WASAPI stream.

### VoiSe.AudioHost

- hidden WinExe with no WinUI dependency;
- resolves the selected device IDs itself;
- creates and owns `Gate2UnifiedAudioEngine`;
- executes SoundBoard, scene, Media Bridge and DSP commands;
- returns status and errors to the UI;
- exits on `Shutdown` or pipe closure.

### VoiSe.Audio

`AudioEngineIpcModels.cs` contains the shared protocol DTOs. The existing audio engine remains the single implementation of mixing and routing behavior.

## Protocol

Each line contains one UTF-8 JSON envelope:

```text
request:  RequestId + Command + serialized payload
response: RequestId + Success + Error + serialized result
```

Commands are serialized through one client lock and processed sequentially by the host. Sound-cache warm-up is scheduled in the host background so it does not block the control pipe.

## Lifecycle

1. UI validates the selected devices.
2. UI creates a random named-pipe server.
3. UI launches `AudioHost\VoiSe.AudioHost.exe --pipe <name>` with Explorer as parent.
4. Host connects to the pipe.
5. UI sends `StartEngine` with device IDs and `EffectSettings`.
6. Host starts microphone capture and both output routes.
7. UI periodically requests a lightweight snapshot.
8. On Stop/Exit, UI sends `Shutdown`, closes the pipe and force-terminates only if graceful exit fails.

## Packaging

For local `dotnet run`, the App project builds the AudioHost project and copies its output to `bin\...\AudioHost`.

For release packaging, `build-installer.ps1` publishes the app and host separately as self-contained x64 payloads. Inno Setup already copies the publish tree recursively.

## Risk

This is an architectural diagnostic build. It is designed to exclude the VoiSee UI process as the owner of any audio stream, but Discord's internal application-audio grouping is not controlled by VoiSee. A real screen-share check is still required.
