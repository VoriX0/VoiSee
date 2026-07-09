# VoiSee 9.1.10 — Theme empty-value reset fix

Gate 9.1.10 reverts the hard-coded Default Dark fallback from 9.1.7 and adds explicit empty-value reset semantics for CSS-like themes.

## Behavior

- `Default Dark` restores original XAML-defined visual values.
- Empty theme rules do not paint VoiSee with fallback colors.
- Empty declaration values reset one property back to its original value.

Example:

```css
#CbSettingsInputMicrophone {
  background:;
  foreground:;
  border:;
  border-radius:;
  padding:;
}
```

If the same property was changed by a previous live theme, saving the empty value returns it to the baseline style captured from VoiSee.

## Why

9.1.7 fixed stale ComboBox/ListView colors by applying an approximate default style to visible controls. That removed the stale color, but it also changed the real default look of Voice Changer, Scenes and other screens. 9.1.10 restores the original approach and uses theme-authored blank selectors for explicit reset instead.
