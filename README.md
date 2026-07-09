# VoiSee 9.1.10 — Theme empty reset startup fix

- Display version: `VoiSee Version 9.1.10`
- Installer output: `VoiSee-Setup-9.1.10-x64.exe`
- Portable output: `VoiSee-Portable-9.1.10-x64.zip`

## Fix

This build fixes the remaining theme reset problem when VoiSee starts with one theme selected and then switches to an empty/reset theme.

Previously, empty declarations could accidentally capture the already-themed value as the default. That left ComboBox controls such as Input Microphone, Monitor Output and the SoundBoard category selector painted with colors from the previous theme.

Now empty values restore the original snapshot when available, and otherwise clear the local themed value so the control can return to its real default style.
