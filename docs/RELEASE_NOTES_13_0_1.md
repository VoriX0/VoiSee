# VoiSee 13.0.1 — Latency Foundation

The first VoiSee 13 build deliberately introduces no AI model runtime yet. It establishes the low-latency audio foundation required by AI Voice.

Highlights:
- real Fast Path that bypasses the Voice Changer DSP processor when the chain is empty;
- Noise Suppression preserved on Fast Path;
- requested WASAPI buffers reduced to 10 ms capture / 20 ms render;
- microphone-route backlog capped at 250 ms instead of ~2 seconds;
- live internal latency breakdown in Advanced Settings;
- Fast/Full route-change diagnostics and high-latency warnings;
- architecture placeholders for Gaming / Balanced / Quality profiles and future AI latency telemetry.

The stable VoiSee 12.5.1 feature set remains the functional baseline. External Windows loopback measurement is required to determine actual end-to-end latency because the Linux build environment cannot run the WinUI/WASAPI application.
