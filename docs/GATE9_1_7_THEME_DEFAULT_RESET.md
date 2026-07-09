# VoiSee 9.1.7 — Theme default reset fix

Gate 9.1.7 fixes stale CSS-like theme styling when switching to Default Dark, deleting the active theme file, or selecting an empty `.voiseetheme.css` file.

## Changes

- Empty theme files now mean: restore VoiSee Default Dark visual baseline.
- ComboBoxes, ListViews, Buttons, ToggleButtons, HyperlinkButtons, Sliders, and TextBoxes get an explicit default-dark reset when there are no active CSS rules.
- Stale ComboBox/category-list colors from a previously selected theme should no longer remain after switching tabs.
- The existing tab-switch theme reapply logic remains unchanged.

## Test

1. Apply a bright theme that changes `.Cb`, `.Bt`, `.Sl`, and the SoundBoard category ComboBox.
2. Switch to an empty theme file or delete all themes and switch tabs.
3. Verify that Input Microphone, Monitor Output, Theme ComboBox, and the SoundBoard category list return to Default Dark.
4. Switch SoundBoard → Voice Changer → Scenes → Settings and verify that stale colors do not return.
