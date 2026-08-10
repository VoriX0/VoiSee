# VoiSee 12.4.0 buildfix 1 — static smoke report

## Scope

- Fix SoundBoard context menu after migration to native `ListView`.
- Remove the Media Bridge scroll container entirely.
- Preserve the validated PerMonitorV2 DPI fix from 12.3.3 buildfix 6 / 12.4.0.

## Root cause fixed

`SelectSound()` rebuilt `SoundItemsListView.Items` on every selection. On a right-click this reset the native ListView viewport and removed the clicked row from the visual tree immediately before `MenuFlyout.ShowAt(row, ...)` used that row as its anchor.

## Checks

- PASS — `SelectSound()` updates row selection visuals in-place and does not call `RebuildSoundRows()`.
- PASS — `OnSoundRowRightTapped()` keeps the clicked row alive through `MenuFlyout.ShowAt(...)`.
- PASS — Media Bridge contains no `ScrollViewer`/`ScrollView` wrapper.
- PASS — MainWindow.xaml parses as XML.
- PASS — no duplicate `x:Name` values.
- PASS — all XAML event handlers referenced by MainWindow.xaml are present in MainWindow.xaml.cs.
- PASS — no `bin` or `obj` directories are packaged.

Full WinUI compilation is not available in the Linux artifact environment; run the normal Windows `dotnet run --project src/VoiSe.App` smoke test after extraction.
