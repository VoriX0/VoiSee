# Gate 12.3.1 — Native scrolling rework

## Problem

VoiSee accumulated several overlapping mouse-wheel systems over earlier gates:

- a global `WH_MOUSE_LL` hook;
- manual screen/client/DPI/XAML coordinate conversion;
- synthetic wheel zones with expansion/shift coefficients;
- native WinUI `ScrollViewer` wheel behavior;
- extra local `PointerWheelChanged` routing in some dialogs.

Those paths could compete and made scroll behavior depend on window geometry and layout changes.

## New ownership rule

Normal application UI uses native XAML scroll ownership. A `ScrollViewer` is both the visual viewport and the wheel-input owner for that area. The legacy low-level mouse-wheel hook is not installed at startup in 12.3.1.

## Voice Changer

The three columns now each have one full-column native vertical scroller:

- `VoicePresetListScrollViewer` owns the complete left column, including Noise Suppression and Presets;
- `ProcessingChainScrollViewer` owns the complete center column, including toolbar, microphone node, chain, and output node;
- `EffectLibraryScrollViewer` owns the complete right column, including search/filter controls and the effect catalog.

The former inner-only scrollers were removed, so wheel ownership matches the visible column geometry instead of a smaller nested list.

The old invisible `VoicePresetWheelColumnZone`, `ProcessingChainWheelColumnZone`, and `EffectLibraryWheelColumnZone` remain inert compatibility elements in this buildfix; because the low-level hook is not installed they participate in neither hit testing nor scrolling.

## SoundBoard

SoundBoard is the sole intentional local exception. `SoundInputOverlay` is a hit-test surface placed above the visual sound list for drag/drop and click routing, so its routed `PointerWheelChanged` event forwards locally to `SoundOverlayScrollViewer`. This uses XAML event routing only and does not calculate screen-coordinate wheel zones.

## Scenes / Settings / dialogs

- `ScenesListView` uses its built-in WinUI scrolling.
- `SceneSoundButtonsScrollViewer` uses native WinUI scrolling.
- `SettingsScrollViewer` uses native WinUI scrolling.
- Media Bridge now has `MediaBridgeScrollViewer` around the complete tab content.
- Sound Editor no longer installs an extra manual wheel handler on its `ScrollViewer`.
- Icon picker and log dialogs no longer stack `AttachIconPickerWheelRouting` on top of native `ScrollViewer` handling.

## Preserved behavior

- global keyboard hotkeys remain unchanged;
- SoundBoard overlay input remains functional;
- Voice Changer effect-chain drag/reorder and displaced-card animation from buildfix 4 remain unchanged;
- DSP and preset schema are unchanged.

## Windows validation

Required manual smoke:

1. SoundBoard: wheel over the sound list; drag/drop and selection still work.
2. Voice Changer: wheel independently anywhere over each of the three columns; only that column moves.
3. Scenes: wheel the saved-scene list and the scene sound-button list independently.
4. Media Bridge: shrink the window and verify tab-level vertical scrolling.
5. Settings: wheel through the complete page.
6. Advanced Settings: scroll engine pane and log pane.
7. Fullscreen log: scroll normally.
8. Voice preset icon picker: wheel anywhere inside the picker list.
9. Sound Editor: wheel while pointer is over effects, sliders, waveform area, and regular content.
