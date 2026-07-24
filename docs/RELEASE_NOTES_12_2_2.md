# VoiSee 12.2.2 — Effect card controls and reorder

## Processing-chain interaction

- Dragging starts from the `⋮⋮` handle and shows a semi-transparent copy of the card under the pointer.
- Effects can be dragged from the library into any position of the processing chain.
- Cards already in the chain can be moved by the same handle.
- Card order is now passed to the audio processor, so moving a card changes the real processing order.
- The processing chain is empty by default when no saved 12.2 chain exists.
- Added `Clear chain`.

## Active cards

Each card inside the processing chain now contains:

- effect name;
- slider;
- manual numeric input;
- delete button;
- drag handle.

Library controls no longer become permanent microphone settings. They create one temporary preview effect. When another library effect is adjusted, the previous preview is removed and its library value is reset.

## Slider ranges

Centered effects remain `-100..100`.

One-direction effects use `0..100`:

- Robot;
- Echo;
- Radio;
- Alien;
- Distortion;
- Tremolo;
- Reverb;
- Bit Crusher.

Gate uses `0..100`, where new `0` maps to the previous `-100` value.

Compressor uses `0..200`:

- new `0` = previous `-100`;
- new `100` = previous `0`;
- new `200` = previous `+100`.

## Persistence

- Ordered active cards and their values are stored in application settings.
- Voice presets now use schema version 2 and store `EffectOrder` plus active values.
- Old schema version 1 presets are migrated when applied.

Build and automated tests were not run.
