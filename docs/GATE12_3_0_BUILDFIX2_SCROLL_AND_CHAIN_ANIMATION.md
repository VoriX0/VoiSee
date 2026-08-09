# VoiSee 12.3.0 buildfix 2 — Voice Changer scroll ownership and chain motion

## Scope

This buildfix keeps the 12.3.0 DSP/preset contract and corrects three Voice Changer UX regressions.

## Independent wheel ownership

The tab-wide `VoiceChangerScrollViewer` is deliberately non-scrollable again.

Three independent vertical scroll targets remain:

1. left preset list (`VoicePresetListScrollViewer`), with the complete left column as its wheel hit-zone;
2. processing chain (`ProcessingChainScrollViewer`), with `ProcessingChainSurface` as its wheel hit-zone;
3. effect library (`EffectLibraryScrollViewer`), with `EffectLibrarySurface` as its wheel hit-zone.

The hit-zones use the exact transformed bounds of the visible panels. No left/right/down expansion and no pixel shift is applied.

The artificial `MinHeight=620` on the Voice Changer workspace was removed so the inner rows can fit the actual tab viewport instead of pushing the preset action buttons below it.

## Drag visual

The custom pointer-drag ghost no longer uses `VoiSee.AccentBrush` for its border. It uses the normal panel border, so dragging does not introduce a blue outline into the default monochrome theme.

## Add / reorder animation

After insertion or reorder, only the affected processing-chain card receives a short motion animation:

- vertical translation from the direction of travel;
- fade from 68% to 100%;
- cubic ease-out;
- a small 2 px settle motion.

No accent-color border flash is used.

## Preserved behavior

- ordered DSP execution;
- schema 2 preset order/value persistence;
- per-effect bypass;
- manual active-card values up to `-10000..+10000`;
- slider remains the convenient bounded control while the text box owns extended numeric entry;
- Noise Suppression Active/Off indicator behavior from 12.3.0.
