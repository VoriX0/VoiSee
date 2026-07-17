# VoiSee 10.1.1 — Connected XAML theme catalogue

## Problem

VoiSee 10.1.0 loaded native `ResourceDictionary` files correctly, but the generated theme file mixed real resources with example keys that were not connected to the current interface. A user could edit a value and see no visible result.

## Goal

A newly created theme must be a practical text-based editor for the current application, not a list of speculative resources.

## Implementation

`Create New Theme` now copies the packaged file:

```text
Themes\UserThemeTemplate.voiseetheme.template
```

into the user's theme directory as a normal:

```text
*.voiseetheme.xaml
```

The theme name placeholder is replaced during creation. The template is copied to both build and publish output.

## File order

The generated XAML is divided into editable sections:

1. Global palette and geometry.
2. Buttons and toggle buttons.
3. Sliders.
4. Text fields and drop-down lists.
5. Lists and list items.
6. Specific VoiSee panels and controls.

The file contains comments explaining which values should be edited and warns the user not to change `x:Key` names.

## Connected controls

The main XAML now uses keyed theme styles through `ThemeResource` for:

- regular buttons;
- SoundBoard transport buttons;
- the global virtual-microphone mute button;
- the SoundBoard edit button;
- scene action buttons;
- Settings and Themes buttons;
- toggle buttons;
- sliders;
- combo boxes;
- text boxes;
- check boxes;
- ListView and ListViewItem;
- secondary status text;
- the main header;
- Settings and About panels.

Standard WinUI visual-state resources are also supplied for button normal/hover/pressed states, slider track/fill/thumb states, text-field focus states, ComboBox states, and list-item hover/selected states.

## Specific VoiSee editors

The final section exposes real resources for:

- main SoundBoard timeline;
- virtual-microphone mute banner;
- drag-and-drop overlay;
- scene/status value cards;
- VB-CABLE notice card;
- main header panel;
- Settings cards;
- virtual-microphone mute button;
- SoundBoard transport and edit actions;
- scene actions;
- engine/settings actions;
- theme-management actions.

## No dead VoiSee keys in the generated template

A static contract check verifies that every `VoiSee.*` key defined by the generated template is referenced by the application or by another connected style/resource. The 10.1.1 template contains 88 `VoiSee.*` keys and no unreferenced keys.

## Live reload

The existing native XAML loader, validation, last-known-good fallback, file watcher, rename, delete, and Default Dark fallback behavior remain unchanged.

## Compatibility

Existing 10.1 user themes remain loadable. Missing new style keys fall back to the built-in `DefaultDark.xaml` dictionary. Old CSS themes remain archived under `Legacy CSS` and are not loaded.

## Manual Windows checks

1. Build and start VoiSee 10.1.1.
2. Create a new theme and confirm the generated file contains all six sections.
3. Change button background, hover color, border, padding, and corner radius.
4. Change slider track, filled track, and thumb colors.
5. Change TextBox and ComboBox background/focus colors and corner radius.
6. Change list background, hover, selected color, item padding, and item radius.
7. Change `VoiSee.Style.Button.SoundBoardTransport` and confirm only transport buttons use that style.
8. Change `VoiSee.Style.Panel.SettingsCard` and confirm the Themes and About cards update.
9. Change timeline, drop overlay, mute banner, and VB-CABLE resources.
10. Break the XAML intentionally and confirm the last working theme stays active.
11. Repair the file and confirm live reload resumes.
12. Switch back to Default Dark and confirm the accepted application appearance returns.
