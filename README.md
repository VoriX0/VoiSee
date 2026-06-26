# VoiSe Gate 5.18 — SoundBoard Red Debug & Wheel Trace

Gate 5.18 is a diagnostic build for finishing the SoundBoard UI.

## Changes

- Window/header version updated to Gate 5.18.
- The red debug overlay for all visible WinUI elements is kept.
- The SoundBoard head timeline block is lifted upward: transport block and timeline block now start at the top of the same row.
- The timeline block height is now the same as the Previous/Next/Stop transport block height, so its bottom edge should align with the bottom edge of Stop.
- Added root-level mouse wheel tracing/routing with `handledEventsToo=true`.
- When the pointer is inside the SoundBoard track-list area, wheel deltas are manually forwarded to the ListView internal ScrollViewer.
- When the pointer is inside the Settings log area, wheel deltas are manually forwarded to the log TextBox internal ScrollViewer.
- Wheel trace counters are shown temporarily in the UI:
  - SoundBoard track wheel events update the transport status text.
  - Settings log wheel events update the engine status text.

## Run

```powershell
dotnet run --project src/VoiSe.App
```

## What to check

1. In fullscreen, check whether red borders show any hidden element over the lower track area.
2. Move the mouse below the 4th track and scroll the wheel.
3. If the wheel event is caught, the SoundBoard status should change to `Track wheel: N`.
4. In Settings, scroll the log area. If the wheel event is caught, the engine status should change to `Log wheel: N`.
5. Check that the bottom edge of the timeline block aligns with the bottom edge of the Stop button, but the whole group is lifted toward the top of the head area.
