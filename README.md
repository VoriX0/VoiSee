# VoiSe Gate 4.2 — Persistent Settings Prototype

Gate 4.2 adds persistent user settings on top of the working Gate 3.6 WinUI 3 control panel.

## What is new

- Saves selected input microphone.
- Saves selected virtual output device, for example `CABLE Input`.
- Saves selected monitoring device, for example headphones.
- Saves `Virtual Mic Master`.
- Saves `Voice Monitor: On/Off`.
- Saves `SoundBoard → Virtual Mic` volume.
- Saves `SoundBoard → Headphones` volume.
- Saves `SoundBoard Virtual Mic Delay`, default 85 ms.
- Saves Voice Changer sliders.
- Saves last selected sound file.

Settings are stored in:

```powershell
%LOCALAPPDATA%\VoiSe\settings.json
```

The app also writes startup diagnostics to:

```powershell
%LOCALAPPDATA%\VoiSe\gate3-startup.log
```

## Run

```powershell
dotnet run --project src/VoiSe.App
```

## Check

1. Select devices in Settings.
2. Set Virtual Mic Master.
3. Enable/disable Voice Monitor in Voice Changer.
4. Select a sound file in SoundBoard.
5. Set SoundBoard route volumes and delay.
6. Close the app.
7. Run it again.
8. Check that settings were restored.

## Notes

Gate 4.2 intentionally keeps the same simple visual layout. The goal is persistence and state stability, not final visual design.


## Gate 4.2 fix

Settings restore now runs in a protected startup phase: scalar controls are restored, device lists are loaded, saved devices are selected by ID/exact name/friendly-name fallback, and autosave is enabled only after restore is complete. This prevents startup defaults from overwriting saved settings.


## Gate 4.2 note
Settings restore is now traced step-by-step. Device enumeration during startup is done on a background task and saved values are applied only after device lists are loaded.
