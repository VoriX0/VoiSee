# VoiSee 9.1.7 — Theme reapply stability and border shorthand

Gate 9.1.7 fixes CSS-like theme stability after tab switching and improves authoring ergonomics.

## Fixed

- If the active `.voiseetheme.css` file is deleted outside VoiSee, the app now clears the active theme and returns to `Default Dark` instead of leaving the old selection in the combo box.
- Empty/non-destructive themes with no active rules restore the default XAML look and clear old theme snapshots.
- Themes are reapplied after tab changes with delayed passes, so controls recreated by WinUI after tab switching keep `.Bt`, `.Pn`, `.Sl`, `.Cb` styling.
- Blank theme templates no longer change the title bar just because `:root` variables exist.

## Added

- Border shorthand:

```css
.Pn { border: solid var(--panel-border) 1; }
.Bt { border: dashed #66FFFFFF 2; }
.Cb { border: none; }
```

Supported style tokens are accepted for readability. WinUI does not draw dashed/dotted borders with the current direct styling engine, but the token is ignored safely and color/width are applied.

## Selector prefixes

- `Pn` — panels and containers
- `Bt` — buttons, toggle buttons and links
- `Sl` — sliders
- `Cb` — combo boxes
- `Txt` — text blocks
- `Tb` — tabs
- `Mn` — menu/context-menu elements where visible to the visual tree

## Slider note

WinUI Slider usually ignores `padding` because the visual rail/thumb are inside the control template. Use `height`, `min-height`, and `margin` instead:

```css
#SlSettingsVirtualMicMaster {
  height: 52;
  min-height: 52;
  margin: 16 0 16 0;
}
```
