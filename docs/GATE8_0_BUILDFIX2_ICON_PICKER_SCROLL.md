# Gate 8.0 buildfix 2 - Voice preset icon picker

This buildfix polishes the Gate 8.0 voice preset icon picker.

## Changes

- Fixed mouse wheel scrolling inside the voice preset icon picker dialog.
- The global Voice Changer wheel router is suppressed while the icon dialog is open, so wheel events reach the picker instead of scrolling the Voice Changer page behind it.
- Changed the picker layout from horizontal overflow columns to vertical scrolling rows.
- Added a local wheel handler to the picker `ScrollViewer` for reliable wheel behavior.
- Removed duplicate built-in white MDL2 icons from the picker.
- Expanded the icon list to 75 entries with 74 unique icons, mostly emoji-style icons requested for preset identities.

## Notes

The default preset icon remains the built-in white microphone symbol.
