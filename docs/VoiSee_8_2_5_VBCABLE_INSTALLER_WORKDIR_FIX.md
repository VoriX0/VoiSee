# VoiSee 8.2.6 — VB-CABLE installer working directory fix

This build fixes VB-CABLE installation from the VoiSee installer checkbox.

## Problem

The VoiSee installer checkbox started `VBCABLE_Setup_x64.exe`, but VB-CABLE reported:

`Missing 'inf' file or Driver package corrupted...`

because the setup executable was launched without the rest of the extracted driver package next to it.

## Fix

- Inno Setup now prefers the setup EXE from the fully extracted VB-CABLE package:
  - `ThirdParty\\VB-CABLE\\_extracted\\VBCABLE_Setup_x64.exe`
  - `ThirdParty\\VB-CABLE\\_extracted\\VBCABLE_Setup.exe`
- The `[Run]` entry now sets `WorkingDir` to the setup EXE directory.
- Root-level setup EXEs are kept only as fallback for already-unpacked VB-CABLE bundles.
- Version updated to `VoiSee Version 8.2.6`.
