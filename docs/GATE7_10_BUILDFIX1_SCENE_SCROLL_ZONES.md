# Gate 7.10 buildfix 1 — Scene scroll zones separation

## Problem

The Scenes tab wheel routing used a Y-only extended zone for `SceneSoundButtonsScrollViewer`.
As a result, the right-side scene sound buttons scroll zone could steal wheel events while the pointer was over the left scene list at the same vertical position.

## Fix

- Added pointer X/Y based element hit testing for scroll zones.
- The left scene list and the right scene sound buttons now own separate horizontal zones.
- Wheel over `ScenesListView` scrolls the scene list only.
- Wheel over `SceneSoundButtonsScrollViewer` scrolls the sound buttons only.
- The scene sound buttons still keep the extended bottom wheel behavior, but only inside their own horizontal bounds.
- Settings scroll routing was switched to the same X/Y bounded helper so it does not accidentally consume wheel input outside the Settings panel.

## Files changed

- `src/VoiSe.App/MainWindow.xaml.cs`
- `VERSION.txt`
