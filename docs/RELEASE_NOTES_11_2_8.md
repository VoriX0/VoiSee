# VoiSee 11.2.8 — Live WASAPI route diagnostics

## Purpose

This is a diagnostic release for the duplicated processed voice heard when the VoiSee window is shared with application audio.

The unsuccessful process-isolation experiments from 11.2.6 and 11.2.7 are not included. The code base returns to the 11.2.5 audio architecture and adds observability and controlled route isolation instead of another speculative redesign.

## Added to Advanced Settings

The left diagnostics panel now refreshes approximately every 750 ms and shows:

- all active Windows Core Audio render and capture endpoints;
- endpoint friendly name, ID, state, current peak, volume and mute state;
- Console, Multimedia and Communications default-role assignments;
- render sessions for each output endpoint;
- process name and PID for each session;
- session state, peak, volume and mute state;
- current VoiSee process ID;
- current engine and route playback states;
- a `Copy Current Snapshot` action for sending the diagnostic report.

## Independent route switches

Two diagnostic switches are available while the engine is running:

- **Enable Virtual Mic Output route** — controls the WASAPI render stream to VB-CABLE;
- **Enable Monitor Output route** — controls the complete physical headphones render stream.

Disabling a route calls `WasapiOut.Stop()` for that output. It is therefore a physical render-session stop rather than a gain change or sample mute. Re-enabling calls `Play()` on the existing initialized output.

The microphone capture, Voice Changer and the other output route remain active. Route states are not saved and return to normal after the engine restarts.

## Retained behavior

- Voice Monitor Off remains a hard disconnect of processed microphone samples from the monitor voice queue.
- SoundBoard-to-headphones remains independent from the Voice Monitor switch.
- The full-width Create / Rename / Delete category button layout is retained.
- Media Bridge and scene integration are unchanged.

## Suggested diagnostic sequence

1. Start the VoiSee audio engine.
2. Open **Settings → Advanced Settings**.
3. Start sharing the VoiSee window with audio in Discord.
4. Confirm the duplicated voice with another participant.
5. Disable only **Monitor Output route** and repeat the phrase.
6. Re-enable it, disable only **Virtual Mic Output route**, and repeat the phrase.
7. Copy or photograph the live endpoint/session report in each state.

Interpretation:

- duplication stops only with Monitor Output disabled — the physical monitor render route is entering the stream;
- duplication stops only with Virtual Mic Output disabled — the VB-CABLE render route is entering the stream;
- duplication remains when either route is disabled separately — Discord is combining another input or a broader system capture path;
- the live session list shows which process Windows associates with each render endpoint during the test.

## Verification status

No build, automated tests, smoke tests, or runtime validation were performed for this package at the user's request.
