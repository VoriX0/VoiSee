# VoiSee 13.1.0 — AI Voice Tab Skeleton

## Added
- New **AI Voice** tab between Voice Changer and Scenes.
- Three-column UI shell: My AI Voices, Online Library, Selected Voice.
- Global Gaming / Balanced / Quality selector, persisted in user settings and connected to the audio-engine performance-profile contract.
- Live AI latency / estimated total latency readout from the 13.0.1 telemetry foundation.
- Voice Monitor control reusing the existing processed-microphone monitor route.
- Compact Model Manager shell and entry points for Create from Audio, Import, Open Folder and Open in Browser.

## Scope
This Gate intentionally contains no AI runtime, model loading, online provider, `.vsee` storage, Quick Clone or Optimize Voice. Placeholder actions make that boundary explicit instead of simulating working AI models.

## Preserved
- VoiSee 13.0.1 Fast Path and latency instrumentation.
- VoiSee 12.x SoundBoard, Voice Changer, Scenes, Media Bridge, DPI and theme fixes.
