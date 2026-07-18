# VoiSee 11.0.1 — Media Bridge layout refinement

## Changes

- Media Bridge preview moved to the left side of the tab.
- The preview image uses `Stretch=Uniform` inside a fully stretched button content area so the selected window is shown completely without cropping.
- The preview itself is now the window-selection control.
- A large centered plus is shown before selection; a smaller centered plus remains over an active preview to communicate that the source can be changed.
- A dedicated control/settings panel was added on the right.
- `Start Broadcast` was merged into the `Play / Pause` button:
  - Play starts a ready source;
  - Pause mutes an active bridge;
  - Play resumes a paused bridge.
- Stop and Play/Pause are displayed at the top of the right panel only after a source is selected.
- The source level indicator is vertical.
- The specification now uses one future Ducking slider only. Attack, Release, Fade In and Fade Out are excluded from the user interface.

## Unchanged

- Process audio capture and the independent media bus.
- No Media Bridge headphone monitoring.
- Pause/Resume global hotkey.
- Stop clears the runtime source.
- No automatic source lookup after VoiSee restart.
- SoundBoard, scenes and microphone routes remain independent.

## Validation

No build, smoke test, automated check or Windows runtime test was performed at the user's request.
