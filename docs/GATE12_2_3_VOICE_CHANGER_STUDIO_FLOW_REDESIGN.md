# Gate 12.2.3 — Voice Changer Studio Flow redesign

## Goal

Replace the 12.2.2 two-column Voice Changer prototype with the approved Studio Flow layout while keeping the working ordered DSP-chain model.

## Layout

The Voice Changer tab is now organized as:

1. **Preset rail on the left**
   - search field;
   - independently scrollable preset list;
   - selected-preset highlight;
   - New / Save Current / Update / Import / Folder actions.

2. **Noise Suppression header**
   - Off / RNNoise / DeepFilterNet selector;
   - Strength slider;
   - runtime status text;
   - dedicated Voice Monitor card.

3. **Horizontal Processing Chain**
   - microphone input node;
   - horizontally scrollable signal lane;
   - active effect cards;
   - output/headphones node;
   - drag-and-drop reorder based on horizontal X position;
   - mouse wheel over the lane scrolls horizontally;
   - Clear Chain and Bypass All controls.

4. **Effect Library below the chain**
   - responsive wrap layout;
   - search;
   - All / Dynamics / Pitch & Tone / Space / Special filters;
   - compact cards retaining the original preview sliders, manual value fields and drag handles.

The approved visual reference is stored at:

`docs/design/VOICE_CHANGER_STUDIO_FLOW_REFERENCE.png`

## Active effect card

Each active chain card contains:

- drag handle;
- compact effect glyph;
- effect name;
- temporary enable/bypass toggle;
- remove button;
- parameter label;
- slider;
- numeric input;
- compact parameter progress strip.

The card size is fixed and the processing lane scrolls horizontally, so the UI does not impose a visual limit on future chain length.

## DSP/order behavior

The 12.2.2 ordered-chain contract is retained:

- `_effectChainOrder` remains the source for ordered stages;
- `EffectSettings.EffectOrder` still receives the visual order;
- Gate and Compressor keep their legacy value conversions;
- the final limiter remains outside the reorderable chain.

The pointer insertion calculation was changed from Y-coordinate midpoint testing to X-coordinate midpoint testing so drag/reorder matches the horizontal signal lane.

## Presets

Preset schema remains version 2. Presets continue to persist:

- `EffectOrder`;
- active effect values.

The new per-card bypass state and global Bypass All state are intentionally session-only in this design iteration and are not added to preset schema 2 yet.

## Scalability note

The UI is horizontally unbounded and can accommodate additional effect types without changing the chain composition. The 12.2.2 data model still allows one active instance per effect key; duplicate instances of the same effect are not introduced by this UI-only redesign.

## Validation in this environment

The container used to prepare the archive does not contain the .NET SDK, so a real WinUI build could not be executed here.

Static checks performed:

- MainWindow XAML parses as XML;
- no duplicate `x:Name` values;
- every XAML event handler resolves to a method in `MainWindow.xaml.cs`;
- all Voice Changer control names required by the existing code are retained;
- theme resources used by the new dynamic cards exist in `DefaultDark.xaml`;
- C# brace balance is intact;
- installer/app/version metadata is synchronized to 12.2.3.

A Windows build and manual interaction test should be run before treating this as a release candidate.
