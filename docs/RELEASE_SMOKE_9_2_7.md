# VoiSee 9.2.7 — release smoke checklist

## Automated static preflight

Result: **36/36 PASS**.

Covered checks:

- version synchronization across `VERSION.txt`, project, XAML, installer, and build script;
- XML/XAML validity, unique XAML names, and handler presence;
- project references and bundled assets;
- PNG/WAV integrity;
- absence of developer/user settings, sounds, scenes, and presets;
- known WinUI compile regressions (`FontWeights`, ambiguous `Path` import);
- Sound Editor minimum selection, preview routing, hotkey isolation, main-timeline isolation, temporary-file cleanup, effects, and `[edit]` naming;
- installer exclusions and build-script sanitization;
- case-insensitive path collisions, obvious secret material, and source delimiter balance.

Two release-preparation defects were found and fixed during the first run:

1. `README.md` still described 9.2.2.
2. `scripts/smoke-installed.ps1` used `Programs\VoiSe`, while the installer uses `Programs\VoiSee`.

## Windows runtime smoke

Run from the repository root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
dotnet clean .\src\VoiSe.App\VoiSe.App.csproj
dotnet build .\src\VoiSe.App\VoiSe.App.csproj -c Release
dotnet run --project .\src\VoiSe.App
```

Verify:

1. The application opens without a console window and shows version 9.2.7.
2. Saved audio devices and scalar settings restore after restart.
3. Voice reaches the virtual microphone; Voice Monitor affects headphones only.
4. A SoundBoard sound plays, pauses, stops, loops, and obeys independent virtual-mic/headphones volumes.
5. Global hotkeys, including assigned NumPad keys, work outside Sound Editor.
6. Sound Editor opens centered; mouse wheel scrolls the editor and does not scroll SoundBoard behind it.
7. Selection duration updates while dragging; preview uses SoundBoard headphones volume and does not reach the virtual microphone.
8. Trim Outside, Cut Selection, Reset, effects, Save File, and Save as work. Repeated Save as produces `[edit]`, `[edit 2]`, and so on.
9. While Sound Editor is open, external sounds/hotkeys are blocked except redirected transport controls.
10. Closing Sound Editor restores normal SoundBoard timeline, wheel routing, and global hotkeys.
11. Global virtual-mic mute silences only the virtual route; local monitoring remains audible.
12. Theme switching and tab switching do not flash the default theme.

## Installer smoke

```powershell
.\scripts\build-installer.ps1
```

Expected files:

```text
artifacts\installer\VoiSee-Portable-9.2.7-x64.zip
artifacts\installer\VoiSee-Setup-9.2.7-x64.exe
```

After installation:

```powershell
.\scripts\smoke-installed.ps1
```

Verify that a clean installation contains no developer categories, personal sounds, scenes, presets, or settings. User data under `%LOCALAPPDATA%\VoiSe` should remain separate from the application directory.
