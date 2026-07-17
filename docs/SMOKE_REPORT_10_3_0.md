# Static smoke report — VoiSee 10.3.0

## Result

**47 / 47 checks PASS**

This report validates the source archive statically. A real WinUI 3 build and runtime test still require Windows with the configured .NET 8 / Windows App SDK toolchain.

## Version and source integrity

1. PASS — `VERSION.txt` is `VoiSee Version 10.3.0`.
2. PASS — README starts with VoiSee 10.3.0.
3. PASS — project `Version` is 10.3.0.
4. PASS — assembly, file and informational versions are synchronized.
5. PASS — Inno Setup version is 10.3.0.
6. PASS — installer build script version is 10.3.0.
7. PASS — visible and startup-log versions are synchronized.
8. PASS — all XML, XAML, project and template files are well formed.
9. PASS — all 34 C# files parse successfully with tree-sitter-c-sharp.
10. PASS — all 60 XAML event-handler references resolve to C# methods.

## Removal of internal SoundBoard dragging

11. PASS — no sound `DragStarting` handler remains.
12. PASS — no `StartDragAsync` call remains.
13. PASS — category drag-over handler removed.
14. PASS — category drop handler removed.
15. PASS — old sound drag-start handler removed.
16. PASS — no `CanDrag="True"` remains in active SoundBoard UI.
17. PASS — no internal drag state remains.
18. PASS — Category ComboBox is no longer a drop target.
19. PASS — context-menu Move and Copy operations remain available.
20. PASS — Explorer-file drag target remains connected.
21. PASS — large 75% import overlay remains.
22. PASS — multiple Explorer `StorageItems` remain supported.

## Tray

23. PASS — tray service provides `Open VoiSee` and `Exit VoiSee`.
24. PASS — separate `Assets/TrayIcon.ico` is included.
25. PASS — tray ICO contains 9 transparent sizes from 16×16 through 256×256.
26. PASS — normal close is intercepted and hides the AppWindow.
27. PASS — restore path shows, restores and foregrounds the existing window.
28. PASS — real Exit closes the window and executes existing cleanup.
29. PASS — hide-to-tray does not stop the audio engine or hooks.

## Single instance

30. PASS — a per-user/per-session named mutex is created before WinUI startup.
31. PASS — activation is transferred through a named pipe.
32. PASS — a secondary process exits before COM wrappers, App, MainWindow, engine or hooks are created.
33. PASS — the primary process dispatches activation to the WinUI thread.

## Background startup and autostart

34. PASS — `--background` is parsed by `Program.Main`.
35. PASS — background mode initializes and hides without intentionally showing the main window; a visible fallback is used if tray setup fails.
36. PASS — autostart uses the current-user Windows Run key.
37. PASS — the executable path is quoted and followed by `--background`.
38. PASS — an existing registration is repaired when the executable path changes.
39. PASS — the Settings checkbox reads actual registry state.
40. PASS — uninstall removes the VoiSee Run value.

## Packaging and regressions

41. PASS — application output remains `WinExe`.
42. PASS — Windows Forms desktop framework is referenced for `NotifyIcon` only.
43. PASS — tray assets are copied to build and publish output.
44. PASS — no `bin`, `obj`, user sounds, categories, scenes, presets or settings are packaged.
45. PASS — publish exclusions for all user data remain in the project.
46. PASS — VoiSee 10.3 implementation documentation is included.
47. PASS — README records the removal of the experimental internal sound drag gesture.

## Required Windows runtime checks

1. Run a clean Release build.
2. Start VoiSee normally and press the window close button; verify the window disappears but audio, active scene loops and global hotkeys keep working.
3. Double-click the tray icon and verify the same window and selected tab return.
4. Test `Open VoiSee` and `Exit VoiSee` from the tray menu.
5. Launch VoiSee a second time while the first copy is visible, minimized and hidden; verify only the original window is activated and no second engine starts.
6. Enable `Start VoiSee with Windows`; inspect the actual HKCU Run value and verify the quoted current executable plus `--background`.
7. Run the registered command and verify no main-window flash is visible, the tray icon appears, and engine/hotkeys initialize.
8. Move or copy a sound using the context menu and verify scene links/hotkeys retain the accepted 10.2.0 behavior.
9. Import multiple WAV/MP3/OGG files from Explorer and verify the large centered overlay still works.
10. Re-run the VoiSee 9.2.7 audio, SoundBoard, Voice Changer, Scenes, themes and Sound Editor regression checklist.

## Environment limitation

The current environment does not contain `dotnet`, MSBuild, Windows App SDK or a Windows shell. Therefore this report does not claim a successful WinUI compilation, tray interaction, registry write, named-pipe activation or audio-runtime test.
