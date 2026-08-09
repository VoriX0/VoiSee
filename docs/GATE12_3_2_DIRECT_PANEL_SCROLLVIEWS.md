# Gate 12.3.2 — Direct panel ScrollViewers

## Goal

Remove geometry-based wheel zones from the target UI areas and make each ScrollViewer the immediate XAML parent of the panel it scrolls.

## Direct parent contracts

- `SoundOverlayScrollViewer -> SoundItemsPanel`
- `VoicePresetListScrollViewer -> VoicePresetsPanel`
- `ProcessingChainScrollViewer -> ProcessingChainContentPanel`
- `EffectLibraryScrollViewer -> EffectLibraryCardsPanel`
- `ScenesListScrollViewer -> ScenesListView`
- `SceneSoundButtonsScrollViewer -> SceneSoundsPanel`
- `SettingsScrollViewer -> SettingsTabRoot`

No Grid/Border intermediary is allowed between these pairs.

## SoundBoard

The legacy transparent `SoundInputOverlay` was removed. `SoundOverlayScrollViewer` now owns mouse interaction, drag/drop and the sound-list viewport directly.

## Voice Changer

The disabled tab-wide `VoiceChangerScrollViewer` and legacy invisible wheel-zone markers were removed. Presets and Effect Library scroll only their content lists, while Noise Suppression and library header/filter controls stay fixed. The processing content panel is directly wrapped by its ScrollViewer.

## Scenes

The scene ListView is directly wrapped by `ScenesListScrollViewer`; the ListView internal scroll mode is disabled to avoid nested wheel ownership. `SceneSoundsPanel` remains the direct child of its own ScrollViewer.

## Settings

`SettingsTabRoot` remains the direct child of `SettingsScrollViewer`, so the entire settings page scrolls as one surface.
