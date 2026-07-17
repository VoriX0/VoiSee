# VoiSee 10.4.1 — Settings layout refinement

## Changes

- Moved the Advanced Settings card from System & Audio to the right column directly below About me.
- Added `SettingsAboutStack` so About me and Advanced Settings remain together in both wide and narrow layouts.
- Replaced the vertical Themes action list with a two-row, three-column grid.
- Removed the Open Theme Template action and its obsolete event handler.
- Removed the visible Virtual Output selector. VB-CABLE render routing remains automatic and uses a hidden compatibility selector so the proven engine/device code is unchanged.
- Removed Virtual Output wording from the Advanced Settings route summary; it now reports VB-CABLE bridge readiness instead.
- Updated engine validation text so users are not told to select an automatic output route.

## Compatibility

- Input Device and Monitor / Headphones remain user-selectable.
- VB-CABLE detection, route persistence, engine startup, tray integration, themes, and external file import remain unchanged.
- No user data is bundled.
