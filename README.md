# VoiSee 8.2.0 — VB-CABLE bundle support

Installer-ready source archive for VoiSee Version 8.2.0.
You can install VoiSee from archives/installer/.exe
Included:
- optional Inno Setup checkbox for bundled VB-CABLE installation
- manual `Install VB-CABLE` button in Settings
- support for extracted VB-CABLE setup or original ZIP package
- safe audio-engine block until VB-CABLE / CABLE Input is detected
- dark UI fix on light Windows theme
- user-generated sounds, categories, presets, scenes, and settings excluded from installer payload

To bundle VB-CABLE, place the official package in:

```text
third_party\VB-CABLE\
```

Then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-installer.ps1
```

Expected installer output:

```text
artifacts\installer\VoiSee-Setup-8.2.0-x64.exe
```
