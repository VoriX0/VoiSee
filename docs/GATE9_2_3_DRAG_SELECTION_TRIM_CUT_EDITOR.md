# Gate 9.2.3 — Drag selection, Trim Outside, and Cut Selection

## Goal
Replace the previous start/end-handle editor with a mouse-selection workflow that behaves more like a conventional waveform editor.

## Interaction model
- Drag across the waveform to select a fragment.
- A single click positions the yellow playhead.
- Dragging the yellow playhead moves it without changing the selection.
- The selected region is shown as a translucent blue overlay without separate start/end handles.
- The minimum selection length is 0.2 seconds where the sound duration permits it.

## Toolbar
The toolbar above the waveform now contains icon-only actions:
- Play from the beginning using the current `SoundBoard → Headphones` volume.
- Play from the beginning of the selected area using the same SoundBoard headphones volume.
- Stop preview.
- Trim Outside — remove everything outside the selection.
- Cut Selection — remove the selected area and join the remaining parts.
- Reset — restore the original source and discard unsaved editor operations.

The old full-volume preview and visible pause button were removed. The configured Play/Pause hotkey remains available inside the isolated editor session for keyboard pause/resume safety.

## Destructive editing safety
- Trim and Cut operate on a temporary working WAV, not directly on the library file.
- The library file changes only after `Save File`.
- `Save as Copy` creates a new library item.
- `Cancel` discards the temporary working files.
- Cut is blocked when fewer than 0.2 seconds would remain.
- Multiple Trim/Cut operations can be performed before saving.
- Reset restores the original source, gain, selection, and playhead.

## Audio routing and hotkeys
- Preview is routed only to headphones.
- Preview always uses the current SoundBoard headphones volume.
- The virtual microphone is not used by editor preview.
- External SoundBoard/Scene sounds and normal VoiSee hotkeys remain blocked while the editor is open.
- The main SoundBoard timeline remains isolated from editor preview.

## Audio processor
Added `SoundCutRequest` and `SoundEditProcessor.RenderCutToWav(...)` to concatenate the samples before and after the selected region into a new temporary WAV.
