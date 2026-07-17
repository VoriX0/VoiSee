# Static smoke report — VoiSee 10.1.3

## Result

**61 / 61 assertions PASS**

## Checks

The assertions below are grouped into 42 readable smoke cases.

1. PASS — archive source is based on VoiSee 10.1.2.
2. PASS — VERSION.txt contains VoiSee Version 10.1.3.
3. PASS — MainWindow title and both visible labels contain 10.1.3.
4. PASS — project Version, AssemblyVersion, FileVersion, and InformationalVersion are 10.1.3.
5. PASS — installer and build-installer script use 10.1.3.
6. PASS — application output type remains WinExe.
7. PASS — DefaultDark.xaml is well-formed XML.
8. PASS — user theme template is well-formed XML.
9. PASS — Neon Cyan sample is well-formed XML.
10. PASS — App.xaml, MainWindow.xaml, and project XML are well formed.
11. PASS — all 31 C# files pass tree-sitter syntax parsing.
12. PASS — Default Dark has no duplicate x:Key values.
13. PASS — user template has no duplicate x:Key values.
14. PASS — Default Dark contains 354 keyed resources.
15. PASS — user template contains 354 keyed resources.
16. PASS — both dictionaries contain 126 editable Color resources.
17. PASS — every editable VoiSee Color resource is referenced by a brush or alias.
18. PASS — template differs from Default Dark only in its explanatory header.
19. PASS — CreateNewThemeFile still writes the complete template.
20. PASS — Open Theme File remains disabled for Default Dark.
21. PASS — button normal background resource exists.
22. PASS — button normal border resource exists.
23. PASS — button normal text resource exists.
24. PASS — button hover background resource exists.
25. PASS — button hover border resource exists.
26. PASS — button hover text resource exists.
27. PASS — button pressed and disabled state resources exist.
28. PASS — editable button corner radius is used by the common style.
29. PASS — implicit Button style covers dynamically created buttons.
30. PASS — ToggleButton checked/hover/pressed resources exist.
31. PASS — SliderThumbBackgroundPointerOver uses VoiSee.Color.SliderThumbHover.
32. PASS — slider track and filled-track normal/hover/pressed/disabled states exist.
33. PASS — ComboBox normal/hover/pressed/focused/disabled resources exist.
34. PASS — ComboBox hover border and hover text resources exist.
35. PASS — dropdown popup background/border/text resources exist.
36. PASS — ComboBoxItem hover background/border/text resources exist.
37. PASS — ComboBoxItem selected-hover background/border/text resources exist.
38. PASS — ListView outer background and border resources exist.
39. PASS — scene item hover background/border/text resources exist.
40. PASS — scene selected and selected-hover background/border/text resources exist.
41. PASS — custom ListViewItem template contains explicit hover and selected border setters.
42. PASS — no bin, obj, personal sound library, scene, preset, or settings data is packaged.

## Environment limitation

The current environment does not contain the .NET SDK or the Windows WinUI XAML compiler. A real Windows build and visual interaction test remain required.
