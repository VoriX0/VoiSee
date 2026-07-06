# VoiSee 8.2.3 — simplified VB-CABLE bridge settings

This release changes the VB-CABLE flow from a user-selected Virtual Output device to an automatic bridge status panel.

## Included changes

- Updated display version to `VoiSee Version 8.2.3`.
- Removed the visible `Virtual Output` selector from the normal Settings UI.
- VoiSee still keeps the internal `VirtualOutputComboBox` hidden so existing routing code can auto-select `CABLE Input` safely.
- Added always-visible `Virtual microphone bridge` status panel.
- If VB-CABLE is detected, the panel says everything is working normally and explains which VB-CABLE microphone endpoint to use in target apps.
- If VB-CABLE is missing, the panel says `VB-CABLE is not installed` and shows the `Install VB-CABLE` button.
- The installer `Create a desktop shortcut` task is now checked by default.
- The installer `Install VB-CABLE virtual microphone bridge` task is now checked by default when a bundled VB-CABLE package is present.
- The VB-CABLE installer is started with elevation from the Inno Setup run section.
