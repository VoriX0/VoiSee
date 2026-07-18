# Gate 11.0.2 — Media Bridge preview fit and level meter

## Scope

This build refines only the Media Bridge presentation. The audio capture and mixer behavior from 11.0.1 is unchanged.

## Changes

- The captured window preview is hosted inside a uniform `Viewbox`, so the complete captured frame is scaled into the preview panel without cropping.
- The preview panel remains clickable for selecting or replacing the source window.
- The central plus indicator is displayed only before a source is selected. It is removed as soon as a window is chosen.
- The source level meter is now a full-height vertical meter.
- The meter uses a fixed semantic scale: green for quiet input, yellow for medium input, and red for loud input.
- The numeric level label remains below the meter.

## Intentionally unchanged

- Media Bridge audio capture.
- Play/Pause and Stop behavior.
- Virtual microphone routing.
- SoundBoard, scenes, Voice Changer, tray behavior, and global hotkeys.
