# Static smoke report — VoiSee 10.1.1

**Result: 55/55 PASS**

> This environment cannot run the Windows App SDK XAML compiler. These are static pre-build checks; the Windows build and live-reload checks remain mandatory.

## Checks

1. **PASS** — XML parses: src/VoiSe.App/App.xaml
2. **PASS** — XML parses: src/VoiSe.App/MainWindow.xaml
3. **PASS** — XML parses: src/VoiSe.App/Themes/DefaultDark.xaml
4. **PASS** — XML parses: sample-themes/Neon_Cyan.voiseetheme.xaml
5. **PASS** — XML parses: src/VoiSe.App/Themes/UserThemeTemplate.voiseetheme.template
6. **PASS** — Version 10.1.1 present: VERSION.txt
7. **PASS** — Version 10.1.1 present: README.md
8. **PASS** — Version 10.1.1 present: MainWindow.xaml
9. **PASS** — Version 10.1.1 present: MainWindow.xaml.cs
10. **PASS** — Version 10.1.1 present: VoiSe.App.csproj
11. **PASS** — Version 10.1.1 present: installer
12. **PASS** — Version 10.1.1 present: build script
13. **PASS** — Theme template is packaged as Content
14. **PASS** — ThemeManager reads packaged template
15. **PASS** — Theme name placeholder exists
16. **PASS** — ThemeManager replaces theme name placeholder
17. **PASS** — All referenced styles defined in DefaultDark
18. **PASS** — All referenced styles defined in User template
19. **PASS** — All referenced styles defined in Sample theme
20. **PASS** — All VoiSee ThemeResource references have Default Dark fallback
21. **PASS** — Generated template has no unreferenced VoiSee keys
22. **PASS** — Generated template has expected six ordered sections
23. **PASS** — Template contains button normal/hover/pressed editors
24. **PASS** — Template contains slider track/fill/thumb editors
25. **PASS** — Template contains input normal/hover/focus editors
26. **PASS** — Template contains list normal/hover/selected editors
27. **PASS** — Template contains specific VoiSee action styles
28. **PASS** — Template contains specific VoiSee panel styles
29. **PASS** — Connected UI style: generic Button
30. **PASS** — Connected UI style: SoundBoard transport
31. **PASS** — Connected UI style: virtual mic mute
32. **PASS** — Connected UI style: SoundBoard edit
33. **PASS** — Connected UI style: scene actions
34. **PASS** — Connected UI style: settings actions
35. **PASS** — Connected UI style: theme actions
36. **PASS** — Connected UI style: ToggleButton
37. **PASS** — Connected UI style: Slider
38. **PASS** — Connected UI style: ComboBox
39. **PASS** — Connected UI style: TextBox
40. **PASS** — Connected UI style: CheckBox
41. **PASS** — Connected UI style: ListView
42. **PASS** — Connected UI style: ListViewItem
43. **PASS** — Connected UI style: secondary text
44. **PASS** — Connected UI style: main header
45. **PASS** — Connected UI style: settings cards
46. **PASS** — All MainWindow XAML event handlers exist in code
47. **PASS** — No malformed property-element tags from style injection
48. **PASS** — No duplicate Style attributes
49. **PASS** — Balanced raw braces: ThemeManager.cs — `{=63 }=63`
50. **PASS** — Balanced raw braces: MainWindow.xaml.cs — `{=1533 }=1533`
51. **PASS** — Legacy CSS parser classes absent from active ThemeManager
52. **PASS** — Native XamlReader loading retained
53. **PASS** — Last-working-theme atomic replacement retained
54. **PASS** — No runtime user JSON files included
55. **PASS** — No user audio library files included

## Windows checks still required

1. `dotnet clean .\src\VoiSe.App\VoiSe.App.csproj`
2. `dotnet build .\src\VoiSe.App\VoiSe.App.csproj -c Release`
3. `dotnet run --project .\src\VoiSe.App`
4. Create a new theme and verify every section changes its connected controls.
5. Test live reload, invalid-XAML fallback, rename, delete, and Default Dark fallback.
