# VoiSee 13.2.0 static smoke report

## PASS

- MainWindow.xaml XML parse
- App.xaml XML parse
- VoiSe.App.csproj XML parse
- app.manifest XML parse
- all `x:Name` values unique
- all referenced XAML handlers exist in MainWindow.xaml.cs
- top AI performance panel contains no AI/Total latency labels
- performance panel width is constrained to two thirds of the available row
- Gaming/Balanced/Quality controls use no system focus visual and no checked border
- Create from Audio / Import / Open Folder ratio is 2:1:1
- `.vsee` store/model classes present
- `.vsee` import/export/rename/delete handlers wired
- user-selected storage setting present
- PerMonitorV2 manifest declaration retained

## Environment limitation

A full WinUI Windows build cannot be executed in the current Linux artifact environment because the .NET/Windows App SDK toolchain is not installed. The first compile/run must therefore be performed on Windows.
