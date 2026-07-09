# VoiSee 9.1.5 — Theme stability and selector aliases

Gate 9.1.5 refines the CSS-like theme system from 9.1.x.

## Version

- Display version: `VoiSee Version 9.1.5`
- Installer output: `VoiSee-Setup-9.1.5-x64.exe`
- Portable output: `VoiSee-Portable-9.1.5-x64.zip`

## Main changes

- Fixes stale theme parameters after creating a new blank theme.
- Reapplies the active theme after TabView visual tree changes, so styles are less likely to disappear after switching tabs.
- Adds selector aliases by element type:
  - `Pn` — panels/containers;
  - `Bt` — buttons/toggle buttons/link buttons;
  - `Sl` — sliders;
  - `Cb` — combo boxes/drop-down lists;
  - `Txt` — text;
  - `Tb` — tabs;
  - `Mn` — menu/context menu elements where available.
- Adds friendly IDs such as `#PnMainHeader`, `#BtSettingsMute`, `#SlSettingsVirtualMicMaster`, `#CbTheme`.
- Adds theme properties for element size/layout hints:
  - `width`, `height`, `min-width`, `min-height`, `max-width`, `max-height`, `spacing` / `gap`.
- Updates the generated theme template with the new naming convention.

## Slider note

WinUI Slider does not use `padding` the same way a normal button does. To make a slider larger, use:

```css
#SlSettingsVirtualMicMaster {
  height: 40;
  min-height: 40;
  margin: 8 0;
}
```

## Build

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-installer.ps1
```
