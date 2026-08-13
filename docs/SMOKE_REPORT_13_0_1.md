# VoiSee 13.0.1 — Static Smoke Report

## Environment limitation

The current build environment is Linux and does not have the .NET SDK installed, so a real WinUI/WASAPI compile and runtime smoke cannot be executed here. Windows validation is required with `dotnet run --project .\src\VoiSe.App`.

## Static checks completed

- MainWindow.xaml XML parse: PASS
- App.xaml XML parse: PASS
- VoiSe.App.csproj XML parse: PASS
- VoiSe.Audio.csproj XML parse: PASS
- app.manifest XML parse: PASS
- MainWindow x:Name uniqueness: PASS (262 names, zero duplicates)
- XAML handler resolution: PASS (79 referenced handlers, zero missing)
- PerMonitorV2 manifest baseline preserved
- Gate2UnifiedAudioEngine structural brace check: PASS
- AudioLatencyTelemetry structural brace check: PASS
- FloatSampleQueue structural brace check: PASS
- MainWindow code-behind structural brace check: PASS
- Fast Path guard around `SimpleVoiceProcessor.ProcessInPlace`: PASS
- 10 ms capture target present: PASS
- 20 ms render target present: PASS
- 250 ms microphone queue cap present: PASS
- AI latency placeholder telemetry present: PASS
- Gaming/Balanced/Quality engine contract present: PASS
- `bin` / `obj` directories excluded from release archive: PASS

## Required Windows smoke

1. Run with an empty Processing Chain and verify Advanced Settings reports `Fast`.
2. Enable RNNoise/DeepFilterNet with an empty chain and verify the route stays `Fast`.
3. Add one Voice Changer effect and verify the route becomes `Full`.
4. Remove/bypass the last effect and verify `Full -> Fast` without stale audio/clicks.
5. Compare perceived and externally recorded latency with VoiSee 12.5.1.
6. Verify SoundBoard, Voice Monitor, Scenes, Media Bridge, global hotkeys, themes, 125% DPI and context menus.
7. Watch `Dropped mic samples`; it should remain at zero during normal operation.
