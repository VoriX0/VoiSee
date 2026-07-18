# VoiSee 11.0.0 — Media Bridge Core

This archive starts the VoiSee 11 line with a provider-independent application-audio bridge.

## User-facing changes

- New `Media Bridge` tab.
- Select one visible application window.
- Large periodically refreshed source preview.
- Source title, application process, broadcast duration, state, and live source-level meter.
- `Stop` moved to the top-right above the preview and hidden until a runtime source is selected.
- `Start Broadcast` and `Pause / Resume` controls.
- Dedicated Media Bridge volume in the virtual microphone.
- No Media Bridge headphone monitoring or duplicated playback.
- New global `Pause / Resume Media Bridge` hotkey; no global Stop hotkey.
- Last source description and volume are saved, but PID/HWND are not saved and the source is never searched or reconnected automatically after restart.

## Audio engine

Media Bridge has its own 48 kHz stereo float queue. It is read only by the virtual-microphone route and is mixed beside microphone, SoundBoard, and scene audio before the existing final limiter. Monitor/headphone output does not read this queue.

## Deferred

Normalization, voice ducking, source launch profiles, browser fallback, and scene background integration are intentionally deferred to VoiSee 11.1–11.2.

## Validation

No build, smoke tests, automated checks, or Windows runtime verification were performed for this archive at the user's request.
