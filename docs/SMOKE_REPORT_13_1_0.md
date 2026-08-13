# VoiSee 13.1.0 — Static Smoke Report

## XML / XAML
- src/VoiSe.App/MainWindow.xaml: PASS
- src/VoiSe.App/App.xaml: PASS
- src/VoiSe.App/VoiSe.App.csproj: PASS
- src/VoiSe.App/app.manifest: PASS

- x:Name: 282 total / 282 unique; duplicates: none
- XAML handlers: 88; missing in code-behind: none

## Gate 13.1 checks
- PASS: AI tab between VC and Scenes
- PASS: Performance buttons
- PASS: 3-column AI layout
- PASS: Voice Monitor
- PASS: Open in Browser
- PASS: No processing diagram
- PASS: No GPU/cache visible
- PASS: Profile persisted
- PASS: Latency telemetry UI
- PASS: PerMonitorV2 preserved
- PASS: Legacy scenes tab index updated
- PASS: No AI runtime dependencies introduced

## Runtime note
A full WinUI/Windows App SDK compile and WASAPI runtime smoke must be performed on Windows; this environment does not provide the Windows toolchain.
