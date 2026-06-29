# Gate 8.1 — Audio start optimization and NumPad hotkeys

## Scope

Gate 8.1 focuses on two runtime issues found after Gate 8.0 polish:

1. Starting a SoundBoard or Scene sound could briefly freeze the cursor/UI while the file was decoded and resampled.
2. External/global hotkeys from the numeric keypad could not be assigned reliably.

## Changes

### Non-blocking sound start

- Sound start no longer performs the heavy `PlaySound` path directly on the UI thread.
- `MainWindow.PlaySoundPath(...)` now schedules the engine playback on a background task.
- UI updates and log messages are posted back to the WinUI dispatcher after playback has been queued.

### PCM decode cache

- `SoundFileLoader` now caches decoded/resampled PCM float buffers by:
  - full file path;
  - last-write timestamp;
  - target sample rate;
  - target channel count.
- Replaying the same sound does not decode the same file again.
- Simultaneous requests for the same file share the same `Lazy<float[]>` decode operation.

### Background warm-up

- After the engine starts, the SoundBoard library is warmed in the background.
- After adding or dropping new tracks, the added files are warmed in the background.
- Warm-up is best-effort and never blocks the UI.

### NumPad hotkeys

The hotkey parser/capture now supports numeric keypad keys separately from the top number row:

- `Num0` … `Num9`
- `Num*`
- `Num+`
- `Num-`
- `Num.`
- `Num/`

This works for SoundBoard hotkeys, Scene sound hotkeys, transport hotkeys, and Voice Changer preset hotkeys.

## Notes

- NumPad keys are treated as global-capable keys, unlike plain A-Z text keys which remain local-only without modifiers.
- Numpad Enter may still be indistinguishable from Enter at the current VK-code level; this gate focuses on the numeric/operator keypad keys.
