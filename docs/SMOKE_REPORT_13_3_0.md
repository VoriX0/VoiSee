# VoiSee 13.3.0 static smoke report

Static checks performed in the build workspace:

- MainWindow.xaml / App.xaml / csproj / app.manifest parse as XML.
- 289 unique `x:Name` values; no duplicates.
- 97 referenced XAML handlers; all found in MainWindow.xaml.cs.
- PerMonitorV2 DPI awareness is preserved.
- AI Voice action layout is present: Rename/Delete, Import/Export, Open Folder full width.
- Create from Audio is in a separate right-side panel below Voice Monitor.
- Hugging Face provider abstraction and first provider implementation are present.
- Provider contains Hub model search, browser URL, supported asset detection and model asset download flow.
- Downloaded assets are handed to AiVoiceStore and packaged into `.vsee`.
- build-installer.ps1 uses version 13.3.0 for Version/AssemblyVersion/FileVersion.
- No `bin` or `obj` directories are included.

A full WinUI build and live Hub/network smoke must be run on Windows because this environment does not contain the Windows .NET/WinUI toolchain and does not have direct network access from the build container.
