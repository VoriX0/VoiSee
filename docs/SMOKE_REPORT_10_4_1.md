# VoiSee 10.4.1 — Static smoke report

## Result

**54/54 checks passed.**

## Verified

- Version consistency across `VERSION.txt`, WinUI project metadata, installer and build script.
- Settings no longer exposes a `Virtual Output` selector.
- The existing automatic VB-CABLE render route remains available internally and is not user-configurable.
- Engine validation no longer tells the user to choose a virtual output.
- Advanced route diagnostics report `VB-CABLE bridge` readiness instead of a selectable Virtual Output device.
- Advanced Settings is directly below About me in one responsive right-column stack.
- Narrow Settings layout moves the complete About + Advanced stack below Themes.
- Themes actions are arranged in exactly two rows: three equal buttons above and two equal buttons below.
- `Open Theme Template` and its XAML event handler are removed.
- Input Device, Monitor / Headphones, SoundBoard Delay, hotkeys and autostart remain present.
- Tray single-click behavior, Advanced Settings dialog, logs, external import overlay and removal of internal sound-category drag remain intact.
- All XML/XAML files parse successfully.
- All 34 C# files parse successfully with Tree-sitter.
- All 61 XAML event handlers resolve to C# methods.
- No duplicate `x:Name` values.
- Default theme/template catalogs remain identical and sample themes are present.
- No build output or user-created settings, categories, sounds, presets or scenes are bundled.

## Environment limitation

The current environment does not contain the .NET SDK or Windows App SDK runtime, so a real WinUI build and visual runtime test could not be performed here. Run the Windows commands below after extracting the archive:

```powershell
dotnet clean .\src\VoiSe.App\VoiSe.App.csproj
Remove-Item .\src\VoiSe.App\bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item .\src\VoiSe.App\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet run --project .\src\VoiSe.App
```
