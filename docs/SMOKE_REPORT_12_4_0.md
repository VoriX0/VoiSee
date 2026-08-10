# VoiSee 12.4.0 — static smoke report

- PASS — `MainWindow.xaml` is well-formed XML.
- PASS — no duplicate `x:Name` values in `MainWindow.xaml`.
- PASS — all referenced `On...` XAML event handlers resolve in `MainWindow.xaml.cs`.
- PASS — SoundBoard `OnSoundRowRightTapped` explicitly selects the clicked sound and opens `CreateSoundContextFlyout()` at the pointer position.
- PASS — Media Bridge vertical and horizontal scroll modes are disabled and both scroll bars are hidden.
- PASS — Per-Monitor V2 DPI awareness from 12.3.3 buildfix 6 remains in `app.manifest`.
- PASS — version metadata is synchronized to 12.4.0 in `VERSION.txt`, WinUI project metadata, installer definition, and installer build script.
- PASS — source package contains no `bin` or `obj` directories.

Full WinUI compilation is not available in the Linux artifact environment; run `dotnet run --project src/VoiSe.App` on the Windows development machine for the runtime smoke test.
