# Gate 6.5 — Voice Pitch Shifter

## Goal

Make the voice changer capable of an obvious deep/squeaky voice change, rather than another EQ-style bass/treble/timbre control.

## Changes

- Added connected `Pitch` slider.
- Removed `Timbre` from the active UI.
- Removed `Chorus` from the active UI.
- Kept `Alien`.
- Added a low-latency variable-delay pitch shifter in `SimpleVoiceProcessor`.
- Expanded the SoundBoard wheel zone to the right and downward.

## Active sliders

- Voice Gain
- Gate
- Compressor
- Pitch
- Bass
- Treble
- Distortion
- Robot
- Tremolo
- Echo
- Reverb
- Radio
- Bit Crusher
- Alien

## Notes

The Gate 6.5 pitch shifter is intentionally simple and real-time friendly. It is good enough for MVP testing of deep/squeaky voice presets. A later Gate can replace it with a dedicated third-party DSP backend if higher quality pitch/formant processing is needed.
