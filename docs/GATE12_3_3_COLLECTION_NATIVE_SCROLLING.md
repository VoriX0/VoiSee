# Gate 12.3.3 — Collection-native scrolling

## Goal

Eliminate artificial scroll-zone geometry from the main user-facing lists. The control visible on screen must also be the control that owns wheel input.

## Architecture

Use `ListView` for collections and `ScrollView` for arbitrary complex content.

| Surface | Owner |
|---|---|
| SoundBoard sounds | ListView |
| Voice presets | ListView |
| Processing Chain | ScrollView |
| Effect Library | ListView |
| Saved scenes | ListView |
| Scene sounds | ListView + ItemsWrapGrid |
| Settings | ScrollView |

The Windows low-level mouse hook is not installed for scrolling. Existing legacy helper methods remain compatibility/dead code only and are not the runtime owner of these surfaces.

## Regression constraints

- SoundBoard sound row click/double-click/context actions remain on the row elements.
- Explorer drag/drop into SoundBoard remains attached to the SoundBoard ListView.
- Processing Chain keeps custom card drag/reorder and FLIP-style movement animation.
- Effect Library child Add/drag controls remain interactive inside ListView items.
- Scene selection keeps `SelectionChanged` on `ScenesListView`.
