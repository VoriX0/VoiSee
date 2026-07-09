# VoiSee 9.1.2 — Theme Engine build fix

Gate 9.1.2 fixes the Gate 9.1.1 theme system compile error and keeps Gate 9.0 Global Virtual Mic Mute.

## Version

- `VoiSee Version 9.1.2`
- Installer output: `VoiSee-Setup-9.1.2-x64.exe`
- Portable output: `VoiSee-Portable-9.1.2-x64.zip`

## Changes

- Settings tab is now a 3-column layout: settings, themes, About me.
- Theme panel actions are simplified: Create New Theme, Open Theme File, Open Theme Folder.
- New theme template is non-destructive: creating the first theme no longer restyles the app until the CSS declarations are edited.
- Theme engine can address global panels, tab-specific classes, friendly ids, buttons, sliders, combo boxes, and sound rows.
- Added pseudo-state support: `:hover`, `:pressed`/`:onclick`, `:checked`/`:on`.
- Added CSS-like brush functions: `rgb()`, `rgba()`, and `linear-gradient()`.
- Header is more compact: engine status moved to Settings next to Start/Stop Engine, mute remains in the header.

## Build

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-installer.ps1
```
