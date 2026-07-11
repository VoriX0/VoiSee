# Gate 9.2 Buildfix 1 — Sound Editor compile fix

Display version remains `VoiSee Version 9.2`.

## Fixed

- Restored the WinUI 3 `Microsoft.UI.Text.FontWeights` namespace in `ThemeManager.cs` and `MainWindow.xaml.cs`.
- Added the missing `SoundEditorPreviewPlaybackKey` constant used by headphone-only preview playback.
- Kept preview playback isolated from the main SoundBoard and scene playback keys.

## Expected build command

```powershell
 dotnet run --project src/VoiSe.App
```

The previous `Windows.UI.Text.FontWeights` and missing preview-key compiler errors should no longer occur. The XAML compiler exit code 1 shown after those errors was a cascading build failure.
