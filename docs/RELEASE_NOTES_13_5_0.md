# VoiSee 13.5.0

## Quick Clone lifecycle
- Reference voices expose `Quick` and `Optimize` actions in Selected Voice.
- Quick validates that the stored reference is at least 10 seconds and upgrades the same `.vsee` package in place to `Quick Clone`.
- The package ID/name/reference are preserved; a `quick/profile.json` entry is added.
- Quick Clone can be selected as the active AI Voice state for the future realtime runtime.
- Optimize is visible but intentionally deferred to Gate 13.6; it never pretends to optimize without a real optimization runtime.

## Activation UX
- active voice cards use `Disable` instead of a disabled `Active` button;
- Selected Voice uses `Disable Voice` for an active RVC/Quick/Optimized voice;
- Reference voice cards remain non-activatable until Quick/Optimize preparation.
