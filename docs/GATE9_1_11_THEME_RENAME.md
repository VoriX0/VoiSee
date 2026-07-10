# Gate 9.1.12 — Theme rename

## Changes

- Added `Rename Theme` button to Settings → Appearance / Themes.
- Rename is enabled only for user `.voiseetheme.css` files from the VoiSee themes folder.
- `Default Dark` cannot be renamed.
- The active theme can be renamed without losing the current selection: VoiSee updates saved settings, refreshes the list, reapplies the theme, and restarts live reload watcher.
- Theme names are sanitized before being used as filenames.
- Duplicate names are rejected with a user-facing message.

## Expected version

- UI: `VoiSee Version 9.1.12`
- Installer: `VoiSee-Setup-9.1.12-x64.exe`
- Portable ZIP: `VoiSee-Portable-9.1.12-x64.zip`
