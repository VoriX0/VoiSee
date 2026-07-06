# VoiSee 8.2.3 — installer tasks defaults and VB-CABLE run fix

This build fixes the Inno Setup compile error and updates the release version.

## Included changes

- Updated version to `VoiSee Version 8.2.3`.
- Fixed Inno Setup `[Run]` entry for VB-CABLE setup: `Verb: runas` now uses `Flags: shellexec waituntilterminated`.
- Desktop shortcut task is checked by default.
- VB-CABLE installation task is checked by default when the bundled VB-CABLE package is present.
- Simplified Settings behavior remains:
  - no visible Virtual Output selector;
  - VB-CABLE detected/not installed status panel;
  - manual Install VB-CABLE button.
