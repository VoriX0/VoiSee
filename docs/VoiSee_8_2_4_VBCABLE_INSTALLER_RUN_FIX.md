# VoiSee 8.2.4 — VB-CABLE installer checkbox run fix

This release fixes VB-CABLE installation from the VoiSee installer checkbox.

## What changed

- Updated version to `VoiSee Version 8.2.4`.
- Fixed bundled VB-CABLE setup staging when the user provides the original VB-CABLE ZIP.
- The build script now extracts the VB-CABLE archive and copies the detected setup executable to a stable installer path:
  - `ThirdParty\VB-CABLE\VBCABLE_Setup_x64.exe`, or
  - `ThirdParty\VB-CABLE\VBCABLE_Setup.exe`.
- The Inno Setup script also checks the `_extracted` folder as a fallback.
- The installer task checkboxes remain enabled by default:
  - create desktop shortcut;
  - install VB-CABLE virtual microphone bridge.

## Why this was needed

The previous installer detected `VBCABLE_Setup_x64.exe` inside `ThirdParty\VB-CABLE\_extracted`, but the Inno Setup run step only looked for it directly inside `ThirdParty\VB-CABLE`. As a result, the checkbox could be selected, but the VB-CABLE setup step was skipped.
