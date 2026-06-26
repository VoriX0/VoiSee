# VoiSe Gate 5.19 — SoundBoard Native Scroll Cleanup

Gate 5.19 removes the red diagnostic overlay and simplifies the SoundBoard / Settings scrolling layout.

## Changes

- Window/header version updated to Gate 5.19.
- Removed the red debug borders and overlay canvas.
- Kept the corrected SoundBoard head alignment from Gate 5.18.
- Rebuilt the track list area as a normal stretching `ListView` in the SoundBoard body.
- Removed the fullscreen coordinate-based scroll-zone hacks that caused the active wheel zone to appear shifted upward.
- Added direct wheel handling on the track `ListView` itself.
- Added direct wheel handling on the Settings log text box.
- Kept double-click playback for tracks.
- Kept the custom timeline from the previous working gates.

## Run

```powershell
dotnet run --project src/VoiSe.App
```

## What to check

1. Red borders are gone.
2. Head alignment remains correct.
3. In fullscreen, mouse wheel scrolling works over the actual track list area, including below the 4th track.
4. Settings log scrolling works again.
5. Double-clicking a track starts playback.
