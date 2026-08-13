# Gate 13.6 — Optimize Voice

## Goal

Complete the Reference → Quick Clone → Optimized lifecycle without introducing a fake neural runtime.

## Implemented

- Optimize is available for Reference and Quick Clone voices.
- It starts only after explicit confirmation by the user.
- Main VoiSee tab controls are locked during optimization so the job cannot silently consume resources while the user continues gaming.
- The same `.vsee` ID and filename are retained.
- The source reference remains intact.
- A canonical optimized reference is created at `optimized/reference_48k_mono.wav`.
- An optimization descriptor is created at `optimized/profile.json`.
- Quick profile is removed after successful optimization.
- Voice type becomes `Optimized` and uses the normal Activate/Disable UI.
- If a Quick voice was active, its ID stays active after in-place optimization.

## Deterministic preprocessing

1. Decode stored reference audio.
2. Downmix all channels to mono.
3. Detect usable leading/trailing speech and retain 150 ms guard padding.
4. Require at least 10 seconds of usable post-trim audio.
5. Remove DC offset.
6. Normalize level with a guarded maximum gain.
7. Resample to 48 kHz mono.
8. Write PCM16 canonical reference.
9. Persist quality and preparation metadata.

## Boundary

No neural AI inference or training is claimed in Gate 13.6. This Gate prepares a stable optimized package for the first realtime AI runtime Gate.
