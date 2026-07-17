# VoiSee 10.2.0 — SoundBoard file and category actions

Base: VoiSee 10.1.5 Native XAML Themes closeout.

## Implemented

### Show in File Explorer

The SoundBoard track context menu now contains `Show in File Explorer`.

- Opens `explorer.exe` with `/select,`.
- Selects the actual managed audio file used by the library item.
- Quotes paths containing spaces or Unicode characters.
- If the file is missing, VoiSee shows a visible error and writes the failure to the application log.

### Transfer to Category

The context menu now contains a `Transfer to Category` submenu with:

- `Move...`
- `Copy...`

Both actions show a target-category picker. The current category is excluded.

#### Move

- Changes only `CategoryId` on the existing `SoundBoardSound`.
- Keeps the same sound ID.
- Keeps the same physical file.
- Keeps the hotkey, usage count, creation/update timestamps and scene references.
- Does not restart the audio engine.

#### Copy

- Creates a new `SoundBoardSound` and a physically independent audio file.
- Assigns a new ID and the selected target category.
- Does not copy the hotkey.
- Resets usage count.
- Does not create scene references automatically.
- Uses unique display names:
  - `Name [copy]`
  - `Name [copy 2]`
  - `Name [copy 3]`
- Uses the new ID in the managed filename so Sound Editor changes to the copy cannot modify the original.

After either operation VoiSee opens the target category and selects the resulting track.

## Preserved behavior

- Existing Play, Assign Hotkey, Rename, Edit Sound, Choose Another File and Delete commands remain available.
- SoundBoard playback and hotkeys are unchanged.
- Scene references continue to resolve sounds globally by ID.
- VoiSee 10.1 XAML themes and the three sample themes remain included.

## Not yet included

The following VoiSee 10.2 items are intentionally reserved for the next buildfix:

- dragging existing SoundBoard tracks to the category ComboBox;
- automatic category dropdown opening;
- Ctrl+Drop copy behavior;
- the enlarged 75% import overlay.
