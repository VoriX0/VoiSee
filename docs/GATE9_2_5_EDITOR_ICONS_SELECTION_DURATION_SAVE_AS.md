# Gate 9.2.5 — Editor icons, live selection duration, and Save as naming

Display version: `VoiSee Version 9.2.5`.

## Changes

- Replaced the text-style selection preview glyph with a compact native-looking selection/play icon.
- Replaced the Trim Outside glyph with the WinUI photo-crop icon.
- The duration field opposite Original duration now shows the selected fragment duration and updates continuously while dragging.
- Renamed the secondary action to `Save as`.
- Save as creates visible names using:
  - `Name [edit]`
  - `Name [edit 2]`
  - `Name [edit 3]`
  - and so on, avoiding duplicate SoundBoard display names.
- Saving an already edited copy does not stack repeated `[edit]` labels; numbering continues from the base name.
- Generated WAV filenames use the same unique edited name plus the internal sound ID.
