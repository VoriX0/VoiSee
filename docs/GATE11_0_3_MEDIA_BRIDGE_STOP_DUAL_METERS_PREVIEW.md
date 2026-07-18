# Gate 11.0.3 — Media Bridge stop placement, dual meters and full preview

## UI

`Stop Broadcast` is located above the preview, outside the settings card. The right panel contains the single Play/Pause transport button.

The level area contains two meters:

- Source: peak level from process loopback capture before Media Bridge gain.
- To Mic: peak level after the Media Bridge volume control and pause state.

## Preview

The preview image is laid out directly with `Stretch=Uniform`. Capture dimensions are obtained under a per-monitor-v2 DPI awareness context to avoid DPI-virtualized window rectangles causing only part of a high-DPI window to be rendered into the preview bitmap.

## Scope

No normalization, ducking, scene integration, or other audio processing was added in this revision.
