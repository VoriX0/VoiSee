# VoiSee 9.2

Gate 9.2 adds the first SoundBoard sound editor.

## Highlights

- Edit selected SoundBoard track from the `Edit Track` button or sound context menu.
- Trim start/end.
- Change volume gain in dB.
- Preview edited sound in headphones only.
- `Save File` updates the current SoundBoard item.
- `Save as Copy` creates a duplicated SoundBoard item with `copy` in the name.
- Edited files are rendered as WAV for reliable playback.
- Audio cache and duration cache are invalidated after edits.

Build with:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-installer.ps1
```

Expected installer:

```text
artifacts\installer\VoiSee-Setup-9.2-x64.exe
```
