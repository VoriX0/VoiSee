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
