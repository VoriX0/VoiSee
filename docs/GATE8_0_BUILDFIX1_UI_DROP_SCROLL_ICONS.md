# Gate 8.0 buildfix 1 — UI seam, drop dedupe, scroll tuning, preset icons

## Changes

- SoundBoard wheel scroll speed was reduced from the Gate 8.0 value while keeping it faster than the pre-Gate-8 baseline.
- SoundBoard drag-and-drop now de-duplicates files inside one drop operation by full path.
- A short drop-event guard was added so the same routed drop event cannot add the same batch twice when both the tab root and input overlay receive the event.
- SoundBoard drag/drop handlers now mark routed drag events as handled.
- The native title bar was replaced with an extended custom black title bar so the white separator line under the system title bar is not visible and the title bar blends into the application background.
- Voice Changer preset icon choices were expanded from 12 to 54 choices.
- The preset icon picker now includes a large emoji set in addition to a few built-in MDL2 symbols.
- The icon picker is wrapped in a scrollable area so the larger set remains usable.

## Notes

The default preset icon is still the microphone symbol. Emoji icons use Segoe UI Emoji, while MDL2 icons use Segoe MDL2 Assets.
