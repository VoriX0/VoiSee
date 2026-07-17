# Static smoke report — VoiSee 10.1.4

## Result

**55 / 55 checks passed.**

## Scope

- release version consistency across application, project, installer, and build script;
- XML/XAML parsing for Default Dark, user-theme template, sample theme, App.xaml, MainWindow.xaml, and project file;
- C# syntax parsing for all 31 source files;
- no duplicate XAML resource keys;
- all semantic `VoiSee.Color.*` keys are connected to brushes/styles;
- new-theme template is an exact editable copy of corrected Default Dark;
- neutral 9.2.7-like button, input, ComboBox, dropdown-item, and slider defaults;
- square CheckBox and complete CheckBox state resources;
- no accent pill in ComboBox dropdown items;
- rounded ComboBox and dropdown items;
- no permanent colored outline on Mute or Edit Track;
- scene selected/hover accent behaviour remains intact;
- protected Default Dark still disables Open Theme File, Rename, and Delete;
- no `bin`, `obj`, settings, sound library, presets, scenes, or other user data in the archive.

## Windows checks still required

```powershell
dotnet clean .\src\VoiSe.App\VoiSe.App.csproj
dotnet build .\src\VoiSe.App\VoiSe.App.csproj -c Release
dotnet run --project .\src\VoiSe.App
```

Manual visual checks:

1. Buttons have no visible border in normal, hover, or pressed states.
2. Mute and Edit Track have no permanent colored outline.
3. Track search and Voice Changer value boxes have no cyan hover/focus outline.
4. Slider thumb stays white when hovered and pressed.
5. CheckBox is square.
6. ComboBox is rounded and dark; popup items use neutral grey hover/pressed/selected backgrounds with no cyan border or accent pill.
7. Selected scene remains accent-colored and scene hover outline remains accent-colored.
8. A newly created theme is visually identical to Default Dark.

Existing user themes are intentionally not overwritten; recreate a 10.1.3 test theme to receive the corrected defaults.
