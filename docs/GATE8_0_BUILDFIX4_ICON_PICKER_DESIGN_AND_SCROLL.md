# Gate 8.0 buildfix 4 — Icon picker design rollback and extended wheel zone

## Changes

- Restored the Voice Changer preset icon picker visual layout to the buildfix 2 style:
  - compact 44x44 icon toggle buttons;
  - original spacing from the 52x52 wrap grid cells;
  - no full-cell stretched button layout.
- Kept explicit wheel routing for the icon picker buttons and ScrollViewer.
- Added an active icon picker wheel route in the global wheel hook.
- The icon picker wheel zone is extended downward by 100% of the visible picker height.
- While the icon picker dialog is open, Voice Changer page scrolling stays suppressed so it does not steal wheel input from the picker.

## Notes

This is a targeted buildfix over Gate 8.0 buildfix 3. It only changes the Voice Changer preset icon picker layout and wheel routing.
