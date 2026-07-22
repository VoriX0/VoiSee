# Gate 12.0.1 — Low-frequency cleanup

## Purpose

Add a second utility cleanup stage before moving to the modular sound-effect chain. The feature targets low rumble, desk vibration and excessive plosive energy that do not require a second neural noise-suppression engine.

## DSP

A first-order real-time high-pass filter is applied independently to the two 48 kHz microphone channels. The cutoff is constrained to 50–160 Hz. The filter adds no block buffering and therefore no intentional latency.

## Processing order

1. Capture and conversion to 48 kHz stereo float.
2. Low-frequency cleanup.
3. RNNoise suppression.
4. Existing gate and compressor.
5. Existing voice effects.
6. Limiter and routing.

## UX

The Voice Changer tab contains a second cleanup card with:

- On/Off toggle;
- one Cutoff slider;
- live value in Hz.

The setting is global and is not stored inside voice presets.

## Scope

SoundBoard, scene sounds and Media Bridge bypass this stage.

## Verification

No build or tests were run.
