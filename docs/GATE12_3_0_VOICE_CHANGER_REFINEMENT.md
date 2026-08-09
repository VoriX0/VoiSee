# Gate 12.3.0 — Voice Changer Refinement

## Goal

Replace the 12.2.3 horizontal Studio Flow layout with the approved second concept: a three-column workspace built around a vertical, directly interactive signal chain.

## Layout contract

### Left column

- Noise Suppression is a compact card at the top.
- Presets occupy the remaining height.
- Presets have their own search field, vertical scrolling, selected state and action buttons.

### Center column

- Voice Monitor, active-effect count, Bypass All and Clear Chain form the sticky header.
- The signal chain runs vertically from `Microphone In` to `Output / Monitor`.
- Active cards are compact rack rows.
- Each row contains order number, drag grip, icon/name, slider, numeric input, effect bypass and delete.
- `+` insertion nodes sit on the signal line between processing stages.
- The chain owns a dedicated vertical ScrollViewer, so the visual model scales to long chains without widening the tab.

### Right column

- Effect Library is independent from the chain.
- Search and category filtering remain available.
- Effects are grouped into Utility, Dynamics, Tone, Space and Character sections.
- Every library card can be dragged to the chain or added with an explicit `Add` button.
- The library owns its own vertical ScrollViewer.

## Interaction changes

12.2.3 changed effect insertion/reorder hit testing to the X axis for its horizontal lane. 12.3.0 restores Y-axis insertion math because the approved chain is vertical.

The Voice Changer low-level mouse-wheel routing no longer sends the whole tab to one ScrollViewer. It routes independently to:

1. preset list;
2. processing chain;
3. effect library.

This prevents one pane from moving while the pointer is over another pane.

## Preserved contracts

- ordered DSP execution;
- `ActiveVoiceEffectOrder` and active values;
- preset schema 2 `EffectOrder` storage;
- schema 1 compatibility path;
- global Noise Suppression before voice effects;
- per-effect bypass and chain bypass introduced in 12.2.3;
- current one-instance-per-effect-key data model.

The redesign does not alter the underlying DSP algorithms.


## Refinements in 12.3.0

- enabled root vertical scrolling to prevent clipped lower controls;
- wheel zones now use the full panel surfaces for preset, chain, and library columns;
- right effect-library panel widened;
- active effect cards allow manual numeric entry in the range -10000..10000 while the slider continues to show the clamped UI range;
- simplified monochrome card borders and preset selection styling;
- removed visible plus markers between processing-chain cards and replaced them with softer separators;
- added a lightweight attention animation when cards are inserted or moved;
- noise-suppression Active indicator now switches to Off when suppression is disabled.
