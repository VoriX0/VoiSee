# Gate 9.1.3 — Theme delete and sample CSS smoke themes

Gate 9.1.3 refines the CSS-like theme workflow after the 9.1.2 build fix.

## Version

- App display version: `VoiSee Version 9.1.3`
- Installer output: `VoiSee-Setup-9.1.3-x64.exe`
- Portable output: `VoiSee-Portable-9.1.3-x64.zip`

## Changes

- Added `Delete Theme` in Settings → Appearance / Themes.
- Deleting a theme requires a confirmation dialog.
- Built-in `Default Dark` cannot be deleted.
- If the active theme is deleted, VoiSee switches back to `Default Dark`.
- Added sample themes in `sample-themes/`:
  - `Neon_Cyan.voiseetheme.css`
  - `Synthwave_Purple.voiseetheme.css`
  - `Terminal_Green.voiseetheme.css`
  - `Amber_Studio.voiseetheme.css`

## Theme smoke tests

1. Copy all files from `sample-themes/` into the VoiSee theme folder opened by `Open Theme Folder`.
2. Open the `Current theme` combo box and verify the new themes appear.
3. Select each theme and check these areas:
   - header
   - mute status
   - SoundBoard
   - Voice Changer
   - Scenes
   - Settings
   - Themes panel
   - About me panel
4. Edit the active `.voiseetheme.css` file, save it, and verify live reload without restarting VoiSee.
5. Check selectors:
   - `.button`
   - `.panel`
   - `.soundboard-button`
   - `.voicechanger-button`
   - `.scenes-button`
   - `.settings-button`
   - `#SoundboardTimeline`
   - `#VBCableNoticeBorder`
   - `#VirtualMicMutedBanner`
6. Check pseudo states:
   - `:hover`
   - `:pressed`
   - `:on` for toggle buttons.
7. Delete a non-active theme and verify it disappears from the combo box.
8. Delete the active theme and verify VoiSee falls back to `Default Dark`.
9. Try deleting `Default Dark` and verify VoiSee shows a message instead of deleting anything.
10. Put a malformed CSS file into the theme folder and verify VoiSee does not crash.

## Notes

The sample themes intentionally use the supported safe subset only: variables, ids/classes, colors, `rgba()`, `linear-gradient()`, borders, opacity, font size/weight, padding/margin, and pseudo states. They do not use scripts, `url()`, external files, or full layout CSS.
