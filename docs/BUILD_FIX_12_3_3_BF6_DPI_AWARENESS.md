# VoiSee 12.3.3 buildfix 6 — Per-Monitor DPI Awareness

Root cause of displaced mouse-wheel / hit-test zones at Windows display scaling above 100% was the custom `app.manifest` not declaring process DPI awareness.

The manifest now declares:

- legacy fallback: `dpiAware = true/pm`
- Windows 10+ preferred mode: `dpiAwareness = PerMonitorV2, PerMonitor`

This keeps WinUI logical DIPs, pointer input and Win32 window coordinates in the correct per-monitor DPI context.

Validation target: Windows display scale 125% (and ideally 150%) without manual wheel-zone offsets.
