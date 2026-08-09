# VoiSee 12.3.3 — Collection-native scrolling

This release replaces the experimental direct ScrollViewer wrapping with controls that own the collection or complex scrolling surface themselves.

## Scroll ownership

- SoundBoard sounds: `ListView` with built-in vertical scrolling.
- Voice presets: `ListView` with built-in vertical scrolling.
- Processing Chain: `ScrollView` directly wrapping `ProcessingChainContentPanel`.
- Effect Library: `ListView`; category sections are direct items.
- Saved scenes: the existing `ListView` now owns scrolling directly; the outer `ScrollViewer` was removed.
- Scene sound buttons: `ListView` with `ItemsWrapGrid`.
- Settings: `ScrollView` directly wrapping `SettingsTabRoot`.

No additional `ScrollViewer` is wrapped around those `ListView` controls. The legacy low-level mouse wheel router remains disabled at startup.
