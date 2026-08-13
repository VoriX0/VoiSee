# VoiSee 13.7.0 — static smoke report

Runtime environment for this archive is Linux without the Windows/.NET SDK or PowerShell, so WinUI compilation and the Windows RVC runtime installation cannot be executed here. The release therefore uses source/static gates before Windows live testing.

## Static gates

- Python bridge parses with the local Python compiler.
- MainWindow XAML parses as XML.
- MainWindow XAML has 293 named controls; all are unique.
- All 102 XAML `On...` event-handler names resolve to methods in MainWindow.xaml.cs.
- `PerMonitorV2, PerMonitor` DPI manifest remains present.
- AI runtime scripts are copied to build/publish output by VoiSe.App.csproj.
- RVC MIT notice is included under ThirdPartyLicenses.
- No `bin` or `obj` directories are included in the source archive.

## Required Windows live smoke

1. Start VoiSee with no active AI voice: existing Fast/Full routes must behave exactly as 13.6.1.
2. Select an RVC `.vsee` and press Activate Voice with no runtime installed: installer prompt must appear.
3. Complete runtime install/repair and run `scripts/check-ai-runtime.ps1`: expected `AI_RUNTIME_CHECK=PASS`.
4. Activate the RVC voice again: Selected Voice/card must switch to Disable; Advanced latency path must become `Ai` or `AiFull`.
5. Speak with Voice Monitor On: monitored speech must be the converted output; SoundBoard routing remains independent.
6. Enable an ordinary Voice Changer effect: path must become `AiFull` and effect must process the AI-converted voice.
7. Disable the RVC voice: the Python process must stop and path return to Fast/Full without restarting VoiSee.
8. Switch Gaming/Balanced/Quality while active: runtime reloads the same voice with the new profile and resumes.
9. Force/observe overload if possible: no raw-mic bypass; diagnostics/log should show underrun/overload warning.
10. Restart VoiSee with an active RVC voice and installed runtime: the same AI voice should restore after the audio engine starts.

- Runtime completeness check includes both RMVPE PyTorch and DirectML ONNX assets.
