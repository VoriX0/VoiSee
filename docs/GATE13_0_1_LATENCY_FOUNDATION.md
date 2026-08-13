# Gate 13.0.1 — Latency Foundation

VoiSee 13 starts by reducing and measuring the latency of the existing microphone path before any AI Voice runtime is introduced.

## Changes

- Added a real `Fast` microphone path. When the Processing Chain contains no active/non-bypassed effects, `SimpleVoiceProcessor.ProcessInPlace` is not called at all.
- Noise suppression remains active on Fast Path when RNNoise or DeepFilterNet is enabled.
- Added `Full` path for the existing ordered Voice Changer DSP chain. Existing DSP behavior is preserved.
- Re-entering Full Path recreates the stateful voice processor so stale samples from old pitch/delay buffers cannot leak into a new session.
- Reduced requested WASAPI buffer targets from the previous 20 ms capture / 50 ms render to 10 ms capture / 20 ms render.
- Reduced maximum microphone-route queue backlog from approximately 2 seconds to 250 ms. Old samples are dropped when the route cannot keep up instead of allowing seconds of latency to accumulate.
- Added dropped-sample counters for both microphone queues.
- Added `AudioLatencyTelemetry` with capture-block duration, format-conversion CPU time, noise-suppression CPU time, AI placeholder time, DSP CPU time, queue backlog, render target, estimated VoiSee-path latency and dropped samples.
- Added live latency diagnostics to Advanced Settings.
- Added throttled warnings when estimated internal VoiSee latency exceeds 250 ms.
- Added `AiPerformanceProfile` (`Gaming`, `Balanced`, `Quality`) as an engine-level contract for later 13.x gates. Gate 13.0.1 does not change audio behavior from this profile yet.

## Routing

Fast Path:

`Physical Mic -> optional Noise Suppression -> Mixer -> Virtual Mic`

Full Path:

`Physical Mic -> optional Noise Suppression -> Voice Changer DSP -> Mixer -> Virtual Mic`

SoundBoard and Media Bridge remain separate mixer inputs and were not redesigned.

## Important measurement note

The new `Estimated VoiSee path` value is diagnostic, not a substitute for an external audio-to-audio loopback measurement. Device drivers, Windows shared-mode engine periods, VB-CABLE and the receiving application can add latency outside the stages visible to VoiSee.

## Windows smoke test

1. Start VoiSee with no active Processing Chain effects. Advanced Settings should show `Processing path: Fast`.
2. Enable RNNoise or DeepFilterNet while the chain is empty. Path must remain `Fast`.
3. Add one effect card. Path must change to `Full` and a route-change log entry must appear.
4. Bypass/remove the last active card. Path must return to `Fast`.
5. Speak continuously while switching Fast/Full and check for clicks, stale audio, dropouts and dropped-sample growth.
6. Verify SoundBoard, Scenes, Voice Monitor, Media Bridge, theme switching and global hotkeys.
7. Record an external loopback test to establish the real end-to-end latency before/after 13.0.1.
