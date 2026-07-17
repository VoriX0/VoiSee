# Static smoke report — VoiSee 10.1.2

**Result: 34 / 34 PASS**

This report covers source-level checks available in the Linux build environment. A real WinUI build and visual comparison still require Windows.

1. **PASS** — ZIP source tree extracted successfully
2. **PASS** — VERSION.txt contains VoiSee Version 10.1.2
3. **PASS** — MainWindow title and visible version labels contain 10.1.2
4. **PASS** — Project Version/AssemblyVersion/FileVersion/InformationalVersion are synchronized
5. **PASS** — Installer and build-installer script use 10.1.2
6. **PASS** — All project XAML/XML/manifest/csproj files parse as XML
7. **PASS** — UserThemeTemplate parses as ResourceDictionary XML
8. **PASS** — All 31 C# files parse without tree-sitter syntax errors
9. **PASS** — All 58 XAML event handlers exist in MainWindow.xaml.cs
10. **PASS** — DefaultDark.xaml contains no duplicate x:Key values
11. **PASS** — User theme template contains no duplicate x:Key values
12. **PASS** — DefaultDark and generated template expose the same 143 keyed resources
13. **PASS** — DefaultDark and generated template resource values/styles are identical
14. **PASS** — New-theme creation calls CreateDefaultDarkCopyXaml
15. **PASS** — Default export also uses the exact Default Dark copy workflow
16. **PASS** — Old cyan sample values are no longer used as the new-theme template
17. **PASS** — Default scene selected background is explicitly defined
18. **PASS** — Default scene pointer-over background is explicitly defined
19. **PASS** — Default scene pointer-over border is explicitly cyan
20. **PASS** — Default selected-pointer-over and selected-pressed states are explicit
21. **PASS** — Default list item foreground states are explicit
22. **PASS** — Default list selection indicator states are explicit
23. **PASS** — Open Theme File has an x:Name and starts disabled
24. **PASS** — Open Theme File is enabled only for an existing user theme
25. **PASS** — Rename Theme is enabled only for an existing user theme
26. **PASS** — Delete Theme is enabled only for an existing user theme
27. **PASS** — Open Theme File no longer creates a new theme as fallback
28. **PASS** — Open Theme File rejects protected Default Dark in handler
29. **PASS** — Missing selected theme file refreshes the list instead of creating a file
30. **PASS** — Theme help text states that new themes are exact Default Dark copies
31. **PASS** — Theme live reload/watch workflow remains present
32. **PASS** — Invalid XAML keeps the previous working theme workflow
33. **PASS** — No user sounds, scenes, presets, theme files, or settings are bundled
34. **PASS** — Application remains WinExe (no console window)

## Required Windows visual smoke

1. Start with `Default Dark` and capture the Settings and Scenes tabs.
2. Create a new theme and do not edit/save any value manually.
3. Verify Settings and Scenes remain pixel-equivalent to Default Dark.
4. Verify the selected scene has the same cyan-tinted fill.
5. Move the pointer across another scene and verify the same cyan hover border/indicator.
6. Select Default Dark and verify Open Theme File, Rename Theme and Delete Theme are disabled.
7. Select the new user theme and verify all three actions are enabled.
8. Change one resource, save, and verify live reload affects only that resource family.

## Build commands

```powershell
dotnet clean .\src\VoiSe.App\VoiSe.App.csproj
dotnet build .\src\VoiSe.App\VoiSe.App.csproj -c Release
dotnet run --project .\src\VoiSe.App
```
