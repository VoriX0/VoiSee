# VoiSee 9.1.6 — Theme stability and selector aliases

## Why this build exists

9.1.4 made radius work better, but two theme-engine issues remained:

1. Creating a new blank theme could leave some values from the previously selected theme.
2. Some styles could disappear after switching tabs because WinUI may virtualize/recreate parts of the TabView visual tree.

## Fixes

- The theme engine now restores every element it has touched before each new theme application, not only the elements styled in the previous visible tab.
- The current theme is reapplied after TabView selection changes.
- The generated theme template now uses short type prefixes:
  - `Pn` = panel/container;
  - `Bt` = button;
  - `Sl` = slider;
  - `Cb` = combo box;
  - `Txt` = text;
  - `Tb` = tabs;
  - `Mn` = menu/context menu where available.

## Examples

```css
.Pn {
  border-radius: 18;
}

.Bt {
  border-radius: 12;
  padding: 14 7;
}

#SlSettingsVirtualMicMaster {
  height: 40;
  min-height: 40;
  margin: 8 0;
}
```

## Slider note

`padding` on WinUI sliders is not a reliable way to change the visible track. Use `height`, `min-height`, and `margin` instead.
