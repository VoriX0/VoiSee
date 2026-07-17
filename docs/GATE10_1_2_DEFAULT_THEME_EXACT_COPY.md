# VoiSee 10.1.2 — Default theme exact-copy fix

## Problem

VoiSee 10.1.1 generated a user theme from a visually different cyan sample catalogue. Selecting the newly created file therefore changed the interface before the user edited anything. The generated values also made it difficult to understand which appearance belonged to the built-in Default Dark theme.

`Open Theme File` also created a user theme when Default Dark was selected, unlike the protected Rename and Delete actions.

## Changes

- `UserThemeTemplate.voiseetheme.template` is now an exact resource/value copy of `Themes/DefaultDark.xaml`; only the descriptive header contains the generated theme name.
- Creating a theme must leave the complete interface visually unchanged until a value is edited.
- Default Dark explicitly defines normal, pointer-over, pressed, selected, selected-hover and selected-pressed states for list items. This includes the cyan selected-scene background and cyan pointer-over/selection border.
- `Open Theme File`, `Rename Theme` and `Delete Theme` are all disabled for Default Dark.
- The open-file handler no longer creates a theme as a fallback. It only opens an existing selected user-theme file.
- The theme help text now explains the exact-copy behavior.

## Manual checks

1. Delete or deselect old experimental themes and select Default Dark.
2. Capture screenshots of Settings and Scenes.
3. Click `Create New Theme` and close the editor without changing the file.
4. Compare the same tabs: no visual element should change.
5. On Scenes, verify the selected item uses the same cyan-tinted background and pointer-over uses the same cyan border as Default Dark.
6. Select Default Dark and verify Open Theme File, Rename Theme and Delete Theme are disabled.
7. Select a user theme and verify all three actions become enabled.
