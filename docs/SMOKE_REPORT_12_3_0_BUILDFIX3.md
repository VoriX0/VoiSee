# Static smoke — VoiSee 12.3.0 buildfix 3

Static checks performed in the build environment:

- MainWindow.xaml parses as XML.
- No duplicate XAML x:Name values.
- All XAML-referenced C# event handlers are present.
- Voice Changer nested ScrollViewer elements no longer declare their previous PointerWheelChanged handlers.
- Preset footer contains Import and Folder only.
- Preset wheel calibration constants: +30% down, +30% right.
- Effect Library calibration constants: +50% right, +30% down.
- Voice Changer low-level wheel path consumes unmatched wheel input inside its workspace, preventing fallback to a second independent nested scrolling path.
- Root/client DIP normalization is present before element hit testing.
- Processing Chain hit-zone remains its visible surface.

Full Windows App SDK compilation/runtime smoke was not executed in this environment.
