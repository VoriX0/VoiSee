# VoiSee 11.0.3

Media Bridge layout and metering refinement.

## Changes

- Moved `Stop Broadcast` out of the settings card and placed it above the preview in the upper-left area.
- Kept only `Play / Pause` in the right-side control panel.
- Added two adjacent vertical level meters:
  - `Source` — the original level captured from the selected application;
  - `To Mic` — the Media Bridge level after `Volume in Microphone` and pause state, before it is mixed with the microphone and SoundBoard.
- Both meters retain the green → yellow → red scale.
- Replaced the nested `Viewbox` preview layout with a direct `Image` using `Stretch="Uniform"`.
- Window capture now temporarily uses a per-monitor-v2 DPI context while obtaining the source size and rendering the preview. This is intended to prevent partial captures on scaled displays.
- Media Bridge audio routing and scene/SoundBoard independence were not changed.

## Validation

No build, smoke tests, or automated checks were run at the user's request.
