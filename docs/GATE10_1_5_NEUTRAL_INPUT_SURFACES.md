# VoiSee 10.1.5 — Neutral input surfaces and VoiSee 10.1 closeout

## Goal

Remove the remaining blue tint from the Default Dark input controls while retaining the complete editable XAML theme catalogue introduced in VoiSee 10.1.

## Corrected controls

- SoundBoard category selector.
- SoundBoard track search field.
- Settings audio-device selectors.
- Settings theme selector.
- Voice Changer numeric value fields.
- Scene voice-preset selector.
- Every other TextBox and ComboBox using the shared VoiSee theme resources.

## Default values

The normal, pointer-over, pressed, focused and disabled backgrounds now use neutral WinUI dark lightweight-resource values instead of opaque blue-grey colors:

- normal: `#0FFFFFFF`;
- pointer-over: `#15FFFFFF`;
- pressed ComboBox: `#08FFFFFF`;
- focused TextBox: `#B31E1E1E`;
- disabled: `#0BFFFFFF`.

These are alpha-based neutral colors. Against VoiSee's black background they reproduce the neutral grey appearance of the native WinUI controls used by VoiSee 9.2.7 without introducing a blue hue.

## Theme creation

`UserThemeTemplate.voiseetheme.template` contains the same corrected values, so every newly created theme is still an exact editable visual copy of Default Dark.

## Scope

No layout, audio, hotkey, Sound Editor, scene, SoundBoard, installer-data or scrolling behavior was changed. This build closes the VoiSee 10.1 native-XAML-theme stage.
