# VoiSee 9.1.6 — Theme reapply stability and border shorthand

- Display version: `VoiSee Version 9.1.6`
- Installer output: `VoiSee-Setup-9.1.6-x64.exe`
- Portable output: `VoiSee-Portable-9.1.6-x64.zip`

## Main changes

- Fixes theme styles disappearing after switching tabs.
- If the active theme file is deleted outside VoiSee, the app resets to `Default Dark` and updates the theme list.
- Empty new themes are now truly non-destructive and clear previous themed styling.
- Adds CSS-like `border` shorthand: `border: solid #66FFFFFF 1;`.
- Adds `Padding_Stress_Test.voiseetheme.css` for checking tab reapply behavior.

Build with:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-installer.ps1
```
