# VoiSee 10.1.3 — expanded native XAML theme catalogue

## Goal

Make the native XAML theme expose the interaction states that were previously easy to control in the CSS theme engine, without reintroducing CSS or a visual-tree translation layer.

## Implemented

### Buttons

The theme now contains independent colors for:

- normal background, border, and text;
- pointer-over background, border, and text;
- pressed background, border, and text;
- disabled background, border, and text;
- shared button corner radius, border thickness, and padding.

The values are mapped to the standard WinUI lightweight-styling resources (`ButtonBackgroundPointerOver`, `ButtonBorderBrushPointerOver`, `ButtonForegroundPointerOver`, and corresponding normal/pressed/disabled keys). An implicit Button style also applies the VoiSee geometry to buttons created dynamically from C#.

ToggleButton has the same normal states plus editable checked, checked-hover, and checked-pressed states.

### Sliders

Editable values now cover:

- track: normal, hover, pressed, disabled;
- filled track: normal, hover, pressed, disabled;
- thumb: normal, hover, pressed, disabled;
- thumb border.

`VoiSee.Color.SliderThumbHover` directly feeds `SliderThumbBackgroundPointerOver`.

### ComboBox and dropdown lists

Editable values now cover:

- collapsed ComboBox background, border, and text in normal, hover, pressed, focused, and disabled states;
- popup background, border, and text;
- dropdown item background, border, and text in normal, hover, pressed, selected, and selected-hover states;
- item and input corner radii.

### Saved scenes ListView

Editable values now cover:

- list background and outer border;
- item background, border, and text;
- hover, pressed, selected, selected-hover, and selected-pressed states;
- selection indicator and focus border.

A VoiSee ListViewItem template was added because the standard presenter exposes state backgrounds and the selection indicator, but not a distinct editable pointer-over border for the whole item.

### Exact-copy workflow

`UserThemeTemplate.voiseetheme.template` still contains the same 354 keyed resources and 126 editable Color values as `DefaultDark.xaml`; only the explanatory header differs. New themes therefore start visually identical to Default Dark.

The Neon Cyan sample was also rebuilt from the complete catalogue.

## Compatibility

Aliases for the old 10.1.0–10.1.2 color names remain in the dictionary. Older partial XAML themes can still rely on Default Dark for keys they do not override.

## Manual Windows checks

1. Create a new theme and verify there is no visual change.
2. Change `VoiSee.Color.SliderThumbHover`, save, and hover a slider thumb.
3. Change all six normal/hover button colors and `VoiSee.CornerRadius.Button`.
4. Hover and select a saved scene; verify background, border, and text values independently.
5. Hover a closed ComboBox and items in its opened dropdown.
6. Break the XAML intentionally and verify the last valid theme remains active.
