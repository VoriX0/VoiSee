# Static smoke report — VoiSee 12.3.1 native scrolling rework

Static checks are performed in the non-Windows build environment. A full WinUI build still requires Windows with the project SDK/workloads.

Checks:

- MainWindow XAML parses as XML.
- No duplicate `x:Name` values.
- application / installer version is 12.3.1.
- startup no longer calls `InstallSoundBoardWheelHook()`.
- global keyboard hook remains installed.
- Voice Changer contains three enabled full-column ScrollViewer controls.
- old nested Voice Changer preset/chain/library scrollers were removed.
- Media Bridge has an enabled vertical ScrollViewer.
- Sound Editor manual wheel AddHandler was removed.
- icon/log dialog manual `AttachIconPickerWheelRouting` calls were removed.
- SoundBoard retains its local overlay wheel forwarding.
- effect-chain displaced-card animation implementation remains present.

- buildfix-4 displaced-card animation remains present via `CaptureEffectChainYPositions()` and `AnimateEffectCardShiftAsync()`.
