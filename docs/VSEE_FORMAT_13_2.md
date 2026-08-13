# VoiSee 13.2 `.vsee` package format

`.vsee` is the portable AI Voice preset/package format introduced in VoiSee 13.2.

## Container

A `.vsee` file is a ZIP-compatible container. VoiSee owns the extension; users do not need to unpack it manually.

Required entry:

- `manifest.json`

Optional payload directories:

- `reference/` — source/reference audio;
- `model/` — imported model, index, ONNX or future optimized artifacts.

## Manifest schema 1

Core fields:

- `SchemaVersion`
- `Id`
- `Name`
- `VoiceType`
- `Engine`
- `Compatibility`
- `Source`
- `EstimatedAiLatencyMs`
- `CreatedUtc`
- `AssetEntries`
- `ReferenceAudioEntry`
- `Notes`

The package filename is the installed user-facing name. This allows Rename to remain O(1) for large packages: VoiSee renames the package instead of recompressing model payloads.

## 13.2 behavior

- Create from Audio creates a Reference `.vsee` package.
- Import accepts `.vsee`, `.pth`, `.index`, and `.onnx`.
- Raw model files are wrapped into one `.vsee` package.
- Export copies the complete package.
- AI runtime compatibility and inference are intentionally deferred.


## Schema 2 / VoiSee 13.5 Quick Clone fields

Reference packages may be upgraded in place to Quick Clone while preserving the same preset ID and package filename. Added optional manifest fields:

- `QuickProfileEntry` — normally `quick/profile.json`;
- `ReferenceDurationSeconds`;
- `QuickPreparedUtc`.

Quick preparation requires at least 10 seconds of decodable reference audio and adds a small `quick/profile.json` descriptor while retaining the original reference asset. The realtime neural runtime remains a later Gate.

## Schema 3 / VoiSee 13.6 Optimized Voice fields

Reference or Quick Clone packages may be upgraded in place to `Optimized` while preserving the same preset ID and package filename. Added optional manifest fields:

- `OptimizedProfileEntry` — normally `optimized/profile.json`;
- `OptimizedReferenceEntry` — normally `optimized/reference_48k_mono.wav`;
- `OptimizedReferenceDurationSeconds`;
- `ReferenceQuality` — `Good`, `Fair`, or `Poor` based on deterministic reference checks;
- `OptimizedPreparedUtc`;
- `OptimizationDurationMs`.

Optimize is an explicit foreground operation. It decodes the original reference, downmixes to mono, trims leading/trailing silence with guard padding, removes DC offset, normalizes level, linearly resamples to 48 kHz, and writes PCM16 canonical audio. The previous `quick/profile.json` is removed when the optimized profile replaces Quick Clone. The original reference remains in the package.

Schema 3 prepares deterministic reusable runtime input; it does not claim that a neural conversion model has already been inferred or trained. Realtime inference is connected by the later AI runtime Gate.
