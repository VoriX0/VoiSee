# VoiSee 8.2.0 — bundled VB-CABLE install support

This build adds support for bundling the official VB-CABLE package with the VoiSee installer.

## User-facing behavior

If VB-CABLE files are bundled before building the installer:

- Inno Setup shows an optional checkbox: `Install VB-CABLE virtual microphone bridge`.
- If the user enables it, the installer launches `VBCABLE_Setup_x64.exe` after installing VoiSee.
- If the user does not enable it, VoiSee still contains a manual `Install VB-CABLE` button in Settings.
- If VB-CABLE is already detected, VoiSee shows `VB-CABLE is detected` and allows the audio engine to start.
- If VB-CABLE is missing, VoiSee shows `VB-CABLE is not installed` and keeps the audio engine disabled.

## Where to place VB-CABLE

Before running `scripts\build-installer.ps1`, place the official VB-CABLE package here:

```text
third_party\VB-CABLE\
```

Supported layouts:

```text
third_party\VB-CABLE\VBCABLE_Setup_x64.exe
```

or:

```text
third_party\VB-CABLE\VBCABLE_Driver_Pack*.zip
```

The build script copies these files into the app package and extracts the ZIP in the publish output when needed.

## Manual install button

The Settings page has an `Install VB-CABLE` button. It searches in:

```text
{app}\ThirdParty\VB-CABLE\
```

It supports either extracted setup files or a bundled ZIP. If nothing is bundled, it opens the official VB-CABLE download page as a fallback.

## Safety

The audio engine still does not start unless `CABLE Input` / VB-CABLE is detected. Ordinary speakers are not used as a virtual output fallback.
