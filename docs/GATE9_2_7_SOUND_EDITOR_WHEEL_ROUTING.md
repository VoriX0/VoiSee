# Gate 9.2.7 — Sound Editor wheel routing

Display version: `VoiSee Version 9.2.7`.

## Problem

The effects panel makes the Sound Editor taller than the available dialog area. The editor already contained a vertical `ScrollViewer`, but the calibrated low-level SoundBoard wheel router intercepted the mouse wheel first. As a result, the track list behind the modal dialog scrolled while the editor content stayed still.

## Fix

- Added a dedicated active Sound Editor `ScrollViewer` reference.
- While the modal editor is open, low-level wheel events are routed to that viewer before any SoundBoard, Voice Changer, Scenes, Settings, or icon-picker routing.
- Added a local handled `PointerWheelChanged` route as a fallback when the global hook is unavailable.
- Forced a finite editor viewport height so the effects section creates a real vertical scroll range.
- Cleared the modal viewer reference when the editor closes.

The SoundBoard wheel calibration from Gate 6.8 remains unchanged outside the editor.
