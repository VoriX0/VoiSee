# Gate 12.2.1 — Effect card interaction

This iteration validates the interaction model before the ordered DSP-chain refactor.

## Implemented

- Empty visual processing chain between microphone and output icons.
- WinUI drag source on every existing effect card.
- Drop targets on the chain and the final insertion zone.
- Temporary single-effect preview: changing a different library control restores the previous preview and replaces the light preview card.
- A dropped preview becomes a normal chain card and its value is committed through the existing settings path.
- Presets are located below the library in the right column.

## Deferred

- Reordering cards already in the chain.
- Removing cards from the chain.
- Persisting explicit card order.
- Making DSP order follow visual order.

Build and tests were intentionally not run.
