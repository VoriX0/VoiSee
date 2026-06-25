# VoiSe Gate 5 — SoundBoard Library

Gate 5 builds on the successful Gate 4.3 WinUI control panel and adds the first real SoundBoard library.

## What is new

- Window title and header now show **VoiSe Gate 5**.
- SoundBoard tab now has categories and a sound list.
- `Add Sound` copies WAV / MP3 / OGG files into `%LOCALAPPDATA%\VoiSe\sounds`.
- Sound metadata is stored in `%LOCALAPPDATA%\VoiSe\soundboard.json`.
- Last selected category and sound are restored after restart.
- Existing audio route remains the same: microphone + selected SoundBoard sound → unified mixer → limiter → VB-CABLE / headphones.

## Run

```powershell
dotnet run --project src/VoiSe.App
```

## Gate 5 checks

1. Start the app and confirm the title says `VoiSe Gate 5`.
2. Open `SoundBoard`.
3. Add a category or use `Default`.
4. Click `Add Sound` and choose WAV / MP3 / OGG.
5. Confirm the file is copied to `%LOCALAPPDATA%\VoiSe\sounds`.
6. Select the sound and click `Play Selected`.
7. Restart the app and confirm the category and selected sound are restored.
8. Check metadata:

```powershell
Get-Content "$env:LOCALAPPDATA\VoiSe\soundboard.json"
```
