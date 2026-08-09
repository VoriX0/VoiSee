# VoiSee 12.3.0 buildfix 3 — Voice Changer wheel routing

## User-requested calibration

- Effect Library wheel zone: shift by +50% panel width on X and +30% panel height on Y.
- Presets wheel zone: use the Presets panel itself, extend +30% downward and +30% to the right.
- Processing Chain wheel zone: keep aligned to the visible Processing Chain panel.
- Preset footer now contains only `Import` and `Folder`, each taking half of the full footer width.

## Root cause found

Voice Changer had two competing wheel paths:

1. the global `WH_MOUSE_LL` hook converted screen/client mouse coordinates to DIPs and manually called `ScrollViewer.ChangeView`;
2. the nested WinUI ScrollViewer instances also had `PointerWheelChanged` handlers and could receive normal XAML wheel input whenever the low-level hook did not consume the message.

As a result, changing layout geometry could change which path handled a wheel event. The manual path also assumed that client-DIP origin and XAML-root origin were identical.

## Buildfix 3 routing model

Voice Changer now uses one manual wheel owner:

- the global hook routes wheel input for the selected Voice Changer tab;
- the three explicit XAML `PointerWheelChanged` handlers were removed from the Voice Changer ScrollViewer elements;
- inside the Voice Changer workspace, unmatched wheel events are consumed so a nested ScrollViewer cannot start a second independent path;
- hook coordinates are normalized from client DIPs to the actual `XamlRoot.Content` / `RootGrid` origin before hit-testing;
- all Voice Changer calibration ratios are defined as constants near the other wheel-routing constants.

This change is intentionally scoped to Voice Changer. Existing historical SoundBoard, Scenes, Settings, modal editor and icon-picker wheel behavior is not rewritten.

## Runtime verification still required

The current execution environment cannot run the Windows App SDK application. Verify on Windows:

1. Presets scroll in the requested expanded lower/right region.
2. Processing Chain scrolls only in its own panel.
3. Effect Library scrolls in the requested shifted right/down region.
4. A single wheel notch does not cause a double step.
5. SoundBoard / Scenes / Settings wheel behavior remains unchanged.
