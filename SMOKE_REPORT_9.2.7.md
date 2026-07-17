# VoiSee 9.2.7 static smoke report

- Total: 36
- Passed: 36
- Failed: 0

## Results

- **PASS** — VERSION.txt parse — `VoiSee Version 9.2.7`
- **PASS** — Version sync: csproj Version — `expect 9.2.7`
- **PASS** — Version sync: installer — `expect 9.2.7`
- **PASS** — Version sync: build script — `expect 9.2.7`
- **PASS** — Version sync: XAML title — `expect 9.2.7`
- **PASS** — Version sync: code log — `expect 9.2.7`
- **PASS** — README current version — `README first line: # VoiSee 9.2.7`
- **PASS** — XML/XAML well-formed
- **PASS** — XAML names unique: App.xaml
- **PASS** — XAML handlers exist: App.xaml
- **PASS** — XAML names unique: MainWindow.xaml
- **PASS** — XAML handlers exist: MainWindow.xaml
- **PASS** — Project references exist: src/VoiSe.App/VoiSe.App.csproj
- **PASS** — Project references exist: src/VoiSe.Audio/VoiSe.Audio.csproj
- **PASS** — Project references exist: src/VoiSe.Gate0.Cli/VoiSe.Gate0.Cli.csproj
- **PASS** — Application icon exists — `Assets\AppIcon.ico`
- **PASS** — PNG assets valid
- **PASS** — WAV assets valid
- **PASS** — No user/generated data in source ZIP
- **PASS** — No Windows.UI.Text.FontWeights regression
- **PASS** — No broad Xaml.Shapes import regression
- **PASS** — Sound editor preview key declared
- **PASS** — Minimum selection 0.2s
- **PASS** — Editor wheel routed before SoundBoard
- **PASS** — Editor wheel state restored
- **PASS** — Global hotkeys isolated in editor
- **PASS** — Main SoundBoard timeline suppressed
- **PASS** — Preview virtual mic volume is zero
- **PASS** — Preview uses SoundBoard monitor volume
- **PASS** — Session temp files cleaned
- **PASS** — Save-as edit naming implemented
- **PASS** — Effects rendered on save/preview
- **PASS** — Installed smoke path matches installer — `installer=VoiSee, smoke=VoiSee`
- **PASS** — Installer excludes user data
- **PASS** — Build script sanitizes user data
- **PASS** — C# lexical balance

## Scope limitation

This report validates the source/release archive statically. A real WinUI build, audio-device routing, hotkeys, UI interaction, and installer execution still require Windows.
