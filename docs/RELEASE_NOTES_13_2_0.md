# VoiSee 13.2.0 — AI Voice Storage

## UI refinement

- compact Gaming / Balanced / Quality panel: about one third shorter, lower height, no accent/blue selection outline;
- removed top AI latency / Total latency metrics; latency remains available in the selected-voice details;
- Create from Audio now occupies one half of the left action row, Import and Open Folder share the other half.

## .vsee format

- introduced portable `.vsee` AI Voice packages;
- each package is a single ZIP-compatible container with `manifest.json` plus reference/model/index assets;
- the storage folder is selected by the user and saved in settings;
- damaged or foreign `.vsee` files cannot break application startup.

## Local AI Voice storage

- Create from Audio stores the selected reference audio as a `.vsee` reference voice;
- Import accepts `.vsee`, `.pth`, `.index`, and `.onnx`; raw model files are wrapped into a `.vsee` package;
- My AI Voices is populated from the selected storage directory;
- search and selection are functional.

## Model Manager

- Rename, Export, Open Folder and Delete are functional;
- rename changes only the package filename, so even large model packages do not need to be rewritten;
- export copies the complete portable `.vsee` package.

AI inference, Quick Clone generation and online provider integration remain intentionally deferred to later 13.x Gates.
