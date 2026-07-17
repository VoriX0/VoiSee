# VoiSee 10.1.4 — Default Dark visual parity with 9.2.7

## Goal

Restore the neutral VoiSee 9.2.7 Default Dark appearance while retaining the expanded native XAML theme catalogue introduced in 10.1.3.

## Changes

- Buttons use the former dark fill and no visible normal/hover/pressed outline.
- Mute and Edit Track no longer add a permanent danger/accent outline.
- TextBox and voice value fields use the former dark input surface with no turquoise hover/focus outline.
- ComboBox controls and popup items use neutral dark/grey states; the accent selection pill is disabled; item and popup rounding are explicit.
- Slider thumbs remain white on pointer-over and pressed instead of becoming blue/cyan.
- CheckBox uses an explicit square corner radius and a neutral white/grey palette.
- Scene selection remains intentionally accent-colored, preserving the verified scene-list behaviour from 10.1.2.
- Separate border-thickness keys were added so panel borders remain editable while buttons/inputs/dropdowns are borderless by default.

## Compatibility

Existing user theme files are not overwritten. New themes are exact copies of the corrected Default Dark catalogue.
