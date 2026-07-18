# VoiSee 11.0.0 — Media Bridge Core

## Scope

VoiSee 11.0.0 adds the first independent Media Bridge route. A user selects one visible application window, sees a periodically refreshed preview, and can send that application's process audio to the existing virtual microphone mix.

## Included

- Separate `Media Bridge` tab.
- Visible window selector.
- Large source preview panel.
- Source application, title, PID, state, elapsed broadcast time, and source-level indicator.
- `Start Broadcast`, `Pause / Resume`, and top-right `Stop`.
- `Stop` is visible only while a runtime source is selected.
- Dedicated `Volume in Microphone` control.
- No Media Bridge headphone monitor route.
- Independent audio bus mixed beside microphone, SoundBoard, and scene audio before the final limiter.
- Saved descriptive profile data: last process name, last window title, volume, and Pause/Resume hotkey.
- No PID or HWND persistence and no automatic source search/reconnection after restart.
- One global Media Bridge hotkey: `Pause / Resume`.

## Stop and Pause semantics

- `Pause` clears and mutes only the Media Bridge bus. The source application continues playing.
- `Resume` restores forwarding from the selected process.
- `Stop` ends process capture, clears the runtime source and preview, and leaves only the descriptive saved profile for a future manual selection.

## Deferred to 11.1+

- Normalization.
- Voice ducking, ducking strength, attack, and release.
- Scene background integration and shared `Stop Broadcast` operation.
- Application launch profiles and browser fallbacks.

## Platform boundary

Per-process loopback is gated at runtime to Windows 10 build 20348 or newer. Older systems can run VoiSee, but Media Bridge start is rejected with a clear message.

## Validation status

No build, smoke test, runtime capture test, or automated validation was run for this archive at the user's request. The first verification must be performed on Windows with an active VB-CABLE route and a media application producing audio.
