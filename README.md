# VoiSe Gate 6.17 — Hotkey Capture UX

Gate 6.17 improves the global hotkey workflow on top of Gate 6.16.

## Changes

- Window title/version updated to **VoiSe Gate 6.17**.
- Hotkeys are now assigned by clicking a field and pressing the key or key combination.
- SoundBoard sound hotkeys use the new capture dialog.
- Voice preset hotkeys use the new capture dialog for both Push to talk and Preset select.
- Transport hotkeys now use one combined **Play / Pause** hotkey instead of separate Play and Pause hotkeys.
- Plain single-key hotkeys such as `H` are local-only when other apps are focused, so they no longer steal letters while typing in Telegram/Discord/browser.
- Ctrl/Alt/Shift combinations remain global while VoiSe is running.

## Run

```powershell
dotnet run --project src/VoiSe.App
```
