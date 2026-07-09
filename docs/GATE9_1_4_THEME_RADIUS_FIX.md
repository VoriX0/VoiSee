# VoiSee 9.1.5 — Theme radius fix and rounded sample themes

## Purpose

This build fixes CSS-like rounded corners for theme files.

## Changes

- `border-radius`, `corner-radius`, and `radius` are accepted aliases.
- Rounded corners now apply not only to `Border`, but also to WinUI controls that expose a `CornerRadius` property, such as many buttons and combo boxes.
- Radius shorthand is supported:
  - `border-radius: 12;`
  - `border-radius: 8 12;`
  - `border-radius: 8 12 12 8;`
- The generated theme template now explains that `var(--name)` substitutes variables from `:root`.
- Sample themes now include rounded panels/buttons and a new `Glass_Rounded.voiseetheme.css` test theme.

## Notes

VoiSee Theme CSS is intentionally not full browser CSS. It is a safe subset mapped to WinUI/XAML properties.
