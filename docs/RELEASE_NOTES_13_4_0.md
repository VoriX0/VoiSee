# VoiSee 13.4.0

## AI Voice activation and compatibility groundwork

- Added per-model Activate/Active control to My AI Voices.
- Moved the Imported/Reference type label to the left side of each model card.
- Added a full-width Activate Voice control at the bottom of Selected Voice.
- Active AI Voice ID is persisted in user settings.
- RVC .pth packages are activation candidates; .index remains optional.
- ONNX-only packages are explicitly marked as requiring conversion rather than pretending to be runtime compatible.
- Added visible hover/pressed feedback to local model cards in addition to native ListView selection states.
- Theme changes rebuild AI Voice cards so dynamic controls pick up the current ResourceDictionary.

The activation state prepares a model for the upcoming realtime AI runtime. 13.4.0 does not yet run neural voice conversion.
