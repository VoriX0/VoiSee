# VoiSee 13.6.0 — Optimize Voice preparation

- Optimize Voice is now a real explicit foreground operation for Reference and Quick Clone presets.
- The original reference is decoded and downmixed to mono, leading/trailing silence is trimmed, DC offset is removed, level is normalized, and a canonical 48 kHz mono PCM16 reference is generated.
- `optimized/reference_48k_mono.wav` and `optimized/profile.json` are stored inside the same `.vsee`.
- Quick profile is removed after optimization; the preset keeps the same ID/name and becomes `Optimized`.
- Reference quality and preparation metadata are persisted.
- Optimization never starts automatically in the background. VoiSee controls are locked for the duration of the explicit operation.
- Neural realtime inference is intentionally still deferred to the next runtime Gate.
