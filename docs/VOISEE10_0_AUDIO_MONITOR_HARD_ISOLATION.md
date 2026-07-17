# VoiSee 10.0.0 — Voice monitor hard isolation

## Problem

When Discord uses `CABLE Output` as the microphone and a screen share is started with audio enabled, viewers can hear the processed voice twice. One copy arrives through Discord's microphone track; the second copy can be captured from a physical/system playback route.

The issue was reported while the VoiSee `Voice Monitor` control was off.

## Change

`Voice Monitor Off` now performs a hard route disconnect:

- processed microphone samples are always queued for the virtual microphone;
- processed microphone samples are queued for the physical monitor only while Voice Monitor is enabled;
- disabling Voice Monitor clears the monitor microphone queue immediately;
- the final monitor mixer still keeps `VoiceMonitorGain = 0` as a second safety layer;
- SoundBoard-to-headphones monitoring remains independent and is not disabled.

This removes the possibility of stale or already-buffered processed voice continuing to reach the physical output after Voice Monitor has been turned off.

## Diagnostic logging

The application log now records one of these states when the engine starts and whenever Voice Monitor changes:

```text
Voice monitor route: connected
Voice monitor route: hard disconnected
```

## Required Windows / Discord validation

1. Start VoiSee and verify the log says `Voice monitor route: hard disconnected`.
2. Keep Discord input set to `CABLE Output`.
3. Start a screen share with audio enabled.
4. Speak while Voice Monitor remains off.
5. Confirm that the viewer hears one voice copy.
6. Play a SoundBoard sound and confirm normal routing is unchanged.
7. Enable Voice Monitor and confirm local voice monitoring still works.
8. Disable Voice Monitor and confirm the log returns to `hard disconnected` immediately.

## Follow-up if duplication remains

If the voice still duplicates while the log shows `hard disconnected`, the second copy is not coming from the physical monitor route. The next diagnostic step is to test whether Discord is capturing the shared-mode VoiSee render stream sent to `CABLE Input`; that requires a separate stream-safe virtual-output implementation or an exclusive-mode experiment.
