# Gate 12.2.2 — Effect card controls and reorder

## Goal

Complete the first usable effect-card workflow after the 12.2.1 prototype.

## UI contract

The workspace keeps the processing chain on the left and the effect library with presets on the right.

The processing chain begins with the microphone icon and ends with the output icon. Between them are zero or more active effect cards and, while previewing, one lighter temporary card.

An active effect card has a fixed layout:

1. drag handle;
2. effect title;
3. delete button;
4. slider;
5. manual value field.

Dragging is initiated only from the handle so slider and text-input interaction cannot accidentally move a card.

## Drag implementation

Native data-package drag was replaced for the main pointer interaction by a captured-pointer drag overlay:

- a semi-transparent visual copy is placed in `EffectDragOverlay`;
- the copy follows the pointer while captured;
- releasing over `ProcessingChainSurface` inserts or reorders the effect;
- releasing elsewhere cancels the operation;
- card widgets remain interactive because only the handle captures the pointer.

## Preview model

Library sliders are candidate controls, not active-chain editors.

Only one preview exists at a time:

- changing a library control creates a lighter pseudo-card at the end of the chain;
- changing a different effect removes the previous pseudo-card and restores its library value to default;
- dropping the previewed effect converts its value into a normal active card;
- preview is not saved until the effect is added.

## DSP order

`EffectSettings` now carries an ordered `VoiceEffectKind` list. `SimpleVoiceProcessor` executes active stages in that order and keeps the limiter as the fixed final safety stage.

Bass and Treble were separated into individual processing stages so their cards can be reordered independently.

## Value domains

- Centered: Voice Gain, Pitch, Formant, Bass, Treble — `-100..100`.
- Unipolar: Robot, Echo, Radio, Alien, Distortion, Tremolo, Reverb, Bit Crusher — `0..100`.
- Gate — `0..100`, translated to legacy `-100..0`.
- Compressor — `0..200`, translated to legacy `-100..100`.

## Storage

Application settings store:

- `ActiveVoiceEffectOrder`;
- `ActiveVoiceEffectValues`.

Preset schema 2 stores:

- `EffectOrder`;
- only active effect values.

Schema 1 presets remain readable through legacy-value conversion.

## Not executed

No Windows build or automated tests were run, following the project instruction to avoid long test operations.
