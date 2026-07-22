# VoiSee 12.0.0 — RNNoise Voice Cleanup

## Summary

VoiSee 12 begins the Voice Changer expansion with microphone noise suppression. The first stage deliberately keeps the public controls small and does not yet introduce the planned draggable effect-chain interface.

## User-facing changes

A new **Noise suppression** card is available at the top of the Voice Changer tab.

Controls:

- **On / Off** — enables or bypasses noise suppression.
- **Strength** — blends the original delayed microphone signal with the RNNoise result from 0 to 100%.

Defaults:

- Noise suppression is disabled.
- Strength is 70%.
- Both values are restored after restarting VoiSee.

Noise suppression is a global microphone-cleanup setting. It is intentionally not stored inside individual voice presets, so changing a character preset does not unexpectedly enable, disable, or retune background-noise removal.

## Audio routing

The microphone path is now:

```text
Physical microphone
    → RNNoise voice cleanup
    → existing gate / compressor / voice effects
    → virtual microphone
    → optional Voice Monitor
```

Only microphone samples enter RNNoise. SoundBoard tracks, scene sounds and Media Bridge audio bypass the noise suppressor and retain their original quality.

## Processing behavior

RNNoise processes mono blocks of 480 samples at 48 kHz. VoiSee downmixes the captured microphone to mono, processes complete frames, then sends the cleaned mono signal to both channels of the existing 48 kHz stereo voice bus.

The dry and processed signals are kept time-aligned before the Strength blend. Enabling suppression adds approximately 10 ms of fixed processing latency.

If the RNNoise native library cannot be initialized, VoiSee keeps the microphone path available without suppression and writes the reason to the application log.

## Dependencies and licenses

- RNNoise — BSD 3-Clause.
- YellowDogMan.RRNoise.NET wrapper — MIT.

License texts are included in `ThirdPartyLicenses` and copied into published builds.

## Preserved behavior

- Discord screen-share voice-isolation protection from VoiSee 11.3.0.
- Hard Voice Monitor route disconnect when monitoring is off.
- SoundBoard, scenes, Media Bridge, themes, global hotkeys and sound editing.
- Full-width SoundBoard category buttons.

## Not included yet

- Draggable effect panels.
- User-configurable effect order.
- Additional cleanup modules such as de-esser, expander or automatic gain control.
- New entertainment effects.
- Voice-activity display.

These will be designed and added in later VoiSee 12 stages.

## Verification status

The source archive was prepared without running a build or automated tests, following the project workflow requested by the user.
