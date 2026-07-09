# Gate 9.1.2 — Theme build fix

This update fixes a Windows/WinUI compile error in `ThemeManager.cs`.

## Fixed

- Replaced the invalid snapshot type `Microsoft.UI.Text.FontWeight` with `Windows.UI.Text.FontWeight`.
- Kept `Microsoft.UI.Text.FontWeights` helper usage for values such as `Bold`, `SemiBold`, `Light`, and `Normal`.

## Version

- App display version: `VoiSee Version 9.1.2`
- Installer output: `VoiSee-Setup-9.1.2-x64.exe`
- Portable output: `VoiSee-Portable-9.1.2-x64.zip`
