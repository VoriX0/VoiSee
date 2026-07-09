# VoiSee 9.1.10 — Theme empty reset startup fix

Gate 9.1.10 fixes the remaining CSS-like theme reset issue after app restart.

## Fixed

- Switching from a persisted active theme, such as `Amber_Studio`, to an empty/reset theme no longer keeps old colors on ComboBox controls.
- Empty declarations no longer capture the current themed value as the default.
- If an element has no stored original snapshot, the theme engine clears the local themed value so WinUI/XAML can return the control to its default appearance.
- This targets stale styling on `Input Microphone`, `Monitor Output`, `ThemeComboBox`, and SoundBoard category controls.

## Empty value behavior

```css
.Cb {
  background:;
  foreground:;
  border:;
  border-radius:;
  padding:;
}
```

An empty value means: reset that property. If VoiSee has an original snapshot, it restores it. If no snapshot exists, it clears the local themed value instead of preserving stale colors from the previous theme.
