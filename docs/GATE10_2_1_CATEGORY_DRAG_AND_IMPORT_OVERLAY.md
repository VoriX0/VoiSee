# VoiSee 10.2.1 — category drag-and-drop and full-window import overlay

> **Historical note:** the experimental internal SoundBoard track-to-category drag gesture documented here was removed in VoiSee 10.3.0 at the user’s request. The large external Explorer import overlay remains active.


Base: VoiSee 10.2.0.

## Internal SoundBoard track drag

The transparent SoundBoard input overlay is now also the drag source. This is
important because it already owns click, double-click, context-menu and wheel
input above the custom sound rows.

Workflow:

1. Press and drag an existing SoundBoard row.
2. Move the pointer over the category ComboBox.
3. After 420 ms the ComboBox opens automatically.
4. Drop on a different category.
5. Normal drop performs Move; holding Ctrl performs Copy.

Move reuses the 10.2.0 library operation and therefore keeps the track ID,
hotkey, statistics, timestamps, managed file and scene references. Copy reuses
the independent-file operation and therefore creates a new ID, resets
statistics, omits the hotkey and produces a unique `[copy]` name.

The target item is highlighted using the active XAML theme's
`ComboBoxItemBackgroundPointerOver` brush. The current source category rejects
the operation. Escape or a drop outside the category targets leaves the library
unchanged and clears drag state.

## External file import overlay

The old small label inside the SoundBoard tab was replaced with a RootGrid-level
panel. When supported files are dragged from Explorer, the panel:

- is centered over the complete VoiSee window;
- uses 75% of the current RootGrid width and height;
- keeps a 36 px safety margin on small windows;
- has a large import icon, title and target-category description;
- remains non-hit-testable so the existing drop route continues to receive the
  files;
- appears only for external StorageItems, never for internal track dragging.

Supported formats remain WAV, MP3 and OGG. Multi-file import and duplicate-drop
protection are unchanged.

## Compatibility

- Existing context-menu Move/Copy commands remain available.
- Gate 6.8 SoundBoard wheel behavior remains unchanged.
- Sound Editor, scenes, hotkeys and audio routing are not modified.
- XAML theme resources continue to control the overlay and ComboBox visuals.
