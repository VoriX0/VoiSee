# VoiSee 13.3.0 — Online Library

## UI refinement
- Moved Rename/Delete/Import/Export/Open Folder into the bottom of My AI Voices.
- The five actions use a 2-column, 3-row layout: Rename/Delete, Import/Export, Open Folder full width.
- Moved Create from Audio into its own panel below Voice Monitor.

## Online Library
- Added the first provider abstraction (`IAiVoiceLibraryProvider`).
- Added Hugging Face Hub as the first provider.
- Search, RVC-focused filtering, browser opening, compatibility heuristics and downloads are live.
- Downloaded compatible `.pth`/`.index` or `.onnx` assets are packaged into a local `.vsee` preset.
- Gated/private/unsupported repositories fail safely and do not break the local library.

AI inference remains deferred to the realtime AI backend Gate.
