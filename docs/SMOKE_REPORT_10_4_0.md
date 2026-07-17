# Static smoke report — VoiSee 10.4.0

## Result

```text
57 / 57 PASS
```

The static validation was executed against the complete VoiSee 10.4.0 source
archive after the Settings redesign and single-click tray change.

## Version and packaging

1. PASS — `VERSION.txt` is `VoiSee Version 10.4.0`.
2. PASS — README starts with VoiSee 10.4.0.
3. PASS — project Version is 10.4.0.
4. PASS — AssemblyVersion and FileVersion are 10.4.0.0.
5. PASS — Inno Setup version is 10.4.0.
6. PASS — installer build script version is 10.4.0.
7. PASS — all visible window version labels are 10.4.0.
8. PASS — startup log identifies 10.4.0.
9. PASS — application output remains `WinExe`.

## Tray

10. PASS — tray icon uses `MouseClick`.
11. PASS — only a left click restores the window.
12. PASS — the old `DoubleClick` handler is removed.
13. PASS — `Open VoiSee` and `Exit VoiSee` remain in the context menu.

## Settings — System & Audio

14. PASS — `System & Audio` heading exists.
15. PASS — successful VB-CABLE state says `VB-CABLE is working normally`.
16. PASS — Audio Devices card exists.
17. PASS — Input Device is visible.
18. PASS — Virtual Output is visible.
19. PASS — Monitor / Headphones is visible.
20. PASS — SoundBoard Delay is in Audio Devices.
21. PASS — Hotkeys card exists.
22. PASS — hotkey priority is documented as Transport → Scene → SoundBoard → Voice Preset.
23. PASS — the Windows autostart checkbox is retained.
24. PASS — autostart helper text describes notification-area startup and hotkeys.
25. PASS — a single Advanced Settings button exists.
26. PASS — the main Settings page no longer exposes an Open logs action.
27. PASS — the main Settings page no longer shows the manual engine-control section.
28. PASS — hidden compatibility controls retain established generated fields without exposing them to the user.

## Advanced Settings

29. PASS — the centered Advanced Settings handler exists.
30. PASS — Start, Stop, Restart and Refresh Devices controls exist.
31. PASS — route summary includes Input, Virtual Output, Monitor and route state.
32. PASS — logs support Clear, Copy and Export.
33. PASS — log auto-scroll logic exists.
34. PASS — text-file export exists.
35. PASS — engine and log areas have their own ScrollViewers.

## Adaptive Settings layout

36. PASS — Settings size-change handler is wired.
37. PASS — the normal three-column layout remains.
38. PASS — narrow layouts stack System & Audio, Themes and About me vertically.
39. PASS — horizontal Settings scrolling stays disabled.

## Themes and About

40. PASS — the Themes panel uses `.voiseetheme.xaml` and ResourceDictionary terminology.
41. PASS — the visible Themes panel does not mention CSS.
42. PASS — the actual user themes folder path is shown.
43. PASS — the bundled full theme template can be opened.
44. PASS — Telegram now points to `https://t.me/VoriXdev`.

## Regression guards

45. PASS — the 75% external Explorer import overlay remains.
46. PASS — internal SoundBoard sound-to-category dragging remains removed.
47. PASS — all 11 XML/XAML/project/manifest files parse.
48. PASS — all 34 C# files parse into valid syntax trees.
49. PASS — all 62 XAML event handlers resolve to C# methods.
50. PASS — there are no duplicate `x:Name` values.
51. PASS — Default Dark has no duplicate resource keys.
52. PASS — the user theme template has the same key catalogue as Default Dark.
53. PASS — Cosmic Nebula sample theme remains included.
54. PASS — Inferno sample theme remains included.
55. PASS — Pastel Dream sample theme remains included.
56. PASS — no `bin`, `obj`, runtime data folders or user JSON files are included.
57. PASS — the only WAV files are the two bundled mute cue assets.

## Windows validation still required

The current environment does not contain the Windows .NET SDK or the WinUI XAML
compiler, so it cannot perform a real Windows build or visual/runtime test.
Run on Windows:

```powershell
dotnet clean .\src\VoiSe.App\VoiSe.App.csproj
Remove-Item .\src\VoiSe.App\bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item .\src\VoiSe.App\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet run --project .\src\VoiSe.App
```

Manual checks:

1. Hide VoiSee with the close button and restore it with one left click on the tray icon.
2. Verify right click still opens the tray menu and `Exit VoiSee` fully terminates the process.
3. Open Settings at normal width and verify three columns.
4. Narrow the window and verify the columns stack vertically without clipping.
5. Open Advanced Settings and test Start, Stop, Restart and Refresh Devices.
6. Test Clear, Copy and Export in the log viewer.
7. Confirm SoundBoard Delay remains functional.
8. Confirm `Open Theme Template` launches Notepad.
9. Confirm Telegram opens `https://t.me/VoriXdev`.
