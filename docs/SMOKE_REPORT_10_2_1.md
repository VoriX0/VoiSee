# Static smoke report — VoiSee 10.2.1

> **Historical note:** the experimental internal SoundBoard track-to-category drag gesture documented here was removed in VoiSee 10.3.0 at the user’s request. The large external Explorer import overlay remains active.


## Result

Static package checks: PASS.

## Checked

- version 10.2.1 synchronized in VERSION.txt, README, project metadata, XAML,
  startup log, installer and build script;
- MainWindow.xaml and all other XML/XAML files are well-formed;
- every referenced XAML event handler exists in MainWindow.xaml.cs;
- all C# files parse successfully with tree-sitter-c-sharp;
- the internal drag source is the existing SoundInputOverlay;
- internal drag state is reset after completion/cancellation;
- category ComboBox opens through a 420 ms DispatcherTimer;
- category item targets accept Move and Ctrl+Copy;
- source-category drops are rejected;
- Move/Copy use the same library operations as the tested 10.2.0 context menu;
- external file import still accepts WAV, MP3 and OGG StorageItems;
- internal track dragging cannot activate the external import overlay;
- the import overlay is hosted at RootGrid level and sized to 75% of the window;
- no bin, obj or user library/settings data are included;
- ZIP integrity test passes.

## Windows runtime checks required

1. Drag a track to the category ComboBox and hold for about half a second.
2. Confirm the list opens without releasing the mouse.
3. Drop on another category and verify Move preserves ID/hotkey/scene references.
4. Hold Ctrl and repeat; verify an independent `[copy]` file is created.
5. Press Escape during a drag and verify nothing changes.
6. Drag an external WAV/MP3/OGG file and verify the large centered overlay.
7. Verify the overlay occupies roughly 75% of the complete VoiSee window.
8. Verify normal SoundBoard wheel scrolling and file import still work.

A full WinUI build is not available in the current Linux environment.
