# Static smoke report — VoiSee 10.2.1 buildfix 1

## Result

Static package checks: **14/14 PASS**.

## Root cause addressed

The internal SoundBoard drag source depended on WinUI automatic `CanDrag` gesture recognition through a transparent input overlay that also handled pointer presses. The drag did not reach `DragStarting` during the Windows test.

Buildfix 1 uses explicit gesture detection:

- source sound, pointer ID and press coordinates are stored on left press;
- movement past an 8 px threshold calls `SoundInputOverlay.StartDragAsync`;
- the existing `DragStarting` handler fills the package and enables Move/Copy;
- all gesture state is cleared on release, cancellation, failure and completion.

## Checked

1. All XML/XAML files are well formed.
2. All C# files parse with tree-sitter-c-sharp.
3. Every event handler referenced by MainWindow.xaml exists.
4. `SoundInputOverlay` uses explicit gesture mode (`CanDrag="False"`).
5. `PointerMoved="OnSoundInputOverlayPointerMoved"` is connected.
6. The drag threshold is 8 px.
7. Pointer ID and press position are tracked.
8. `StartDragAsync(pointerPoint)` is called after threshold crossing.
9. The existing `DragStarting` data package remains connected.
10. Move and Copy operations remain available.
11. The 420 ms category ComboBox opening timer remains.
12. Category drop still calls the tested 10.2.0 transfer operation.
13. The external file-import overlay remains separate and present.
14. Buildfix documentation is included.

## Windows runtime checks required

1. Drag a sound row toward the category ComboBox.
2. Confirm the cursor enters a drag operation and the ComboBox opens after about 420 ms.
3. Drop on another category and verify Move.
4. Repeat while holding Ctrl and verify Copy.
5. Verify single-click selection, double-click playback and right-click menu.
6. Verify SoundBoard wheel scrolling and Explorer import remain unchanged.

A full WinUI build is not available in the current Linux environment.
