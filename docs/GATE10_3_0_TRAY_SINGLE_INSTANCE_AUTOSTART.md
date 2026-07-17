# VoiSee 10.3.0 — tray, single instance and autostart

## Scope

This build removes the abandoned internal SoundBoard track-to-category drag gesture.
The tested context-menu Move/Copy operations and the large Explorer import overlay remain.

## Tray

- `AppWindow.Closing` is cancelled unless a real exit was requested.
- Close hides the `AppWindow`; audio, hotkeys, scenes and looped playback stay alive.
- A separate transparent `Assets/TrayIcon.ico` resource is used by `NotifyIcon`.
- The tray menu contains `Open VoiSee`, a separator and `Exit VoiSee`.
- Double-click and Open restore/activate the existing window.
- Exit closes the real window and runs the existing engine/hook/theme-watcher cleanup.

## Single instance

- `Program.Main` acquires a per-user/per-session named mutex before WinUI starts.
- A secondary launch never constructs `App` or `MainWindow` and therefore cannot create
  a second audio engine or register a second set of hooks.
- Activation is forwarded to the primary process through a named pipe.
- The primary process dispatches restore/activation to the WinUI dispatcher.

## Autostart

- Settings contains `Start VoiSee with Windows`.
- State is read from `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- The value is `"<current exe>" --background` and is repaired when the executable path changes.
- Background launch initializes the normal app, audio engine, hooks and tray icon without intentionally showing the window. If tray initialization fails, VoiSee opens visibly instead of leaving an inaccessible background process.
- Uninstall removes the per-user Run value.

## Manual Windows checks

1. Start VoiSee normally; close with X and confirm the process/audio/hotkeys continue.
2. Double-click the tray icon and verify the same window returns on the previous tab.
3. Use Open VoiSee from the tray menu.
4. Launch VoiSee.exe again while hidden and verify the original window is restored.
5. Confirm Task Manager contains only one active VoiSee process after the second launch exits.
6. Enable autostart and verify the actual HKCU Run value contains the current quoted path and `--background`.
7. Sign out/in or run the registered command and verify startup is hidden in tray.
8. Use Exit VoiSee and confirm the process, tray icon, hooks and audio engine are gone.
9. Verify external multi-file drag import and context-menu Move/Copy still work.
10. Verify normal SoundBoard clicking, double-click playback and Gate 6.8 scrolling.

## Validation boundary

Static XML/C# checks pass, but tray, Win32 foreground activation, HKCU registration, single-instance IPC and audio continuity must be verified on Windows.
