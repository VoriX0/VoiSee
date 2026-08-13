# VoiSee 13.5.0 static smoke

- MainWindow.xaml XML parse: PASS
- App.xaml / csproj / manifest XML parse: PASS
- x:Name uniqueness: PASS (292 unique)
- XAML handlers resolved in MainWindow.xaml.cs: PASS (99)
- PerMonitorV2 manifest preserved: PASS
- Active card action uses Disable: PASS
- Reference Selected Voice actions Quick + Optimize: PASS
- Quick Selected Voice actions Activate/Disable + Optimize: PASS
- Imported/Optimized-style action spans full Selected Voice width: PASS
- Quick lifecycle preserves preset ID/package and adds quick/profile.json: static inspection PASS
- Reference minimum duration check: 10 seconds
- bin/obj excluded: PASS

Full WinUI build and audio-decoder/runtime smoke must be run on Windows. Neural realtime inference remains outside Gate 13.5.
