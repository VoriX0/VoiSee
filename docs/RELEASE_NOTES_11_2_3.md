# VoiSee 11.2.3 — Screen-share voice monitor hard isolation

## Reported symptom

When the VoiSee application window is shared with application audio enabled, viewers may hear the processed voice twice: once through the virtual microphone and once through the shared application audio. Sharing an unrelated application does not reproduce the issue. The duplicate voice follows Voice Changer processing, while SoundBoard playback is unaffected.

## Diagnosis

The symptoms point to the processed microphone monitor branch rather than SoundBoard or Media Bridge. Previously, processed voice samples were continuously queued for the physical monitor output even when Voice Monitor was Off; the final monitor mixer then applied a gain of zero. That leaves an unnecessary live render route and may leave buffered samples available to application-audio capture.

## Change

Voice Monitor Off now performs a hard route disconnect:

- processed microphone samples continue to feed the virtual microphone;
- monitor microphone samples are queued only while Voice Monitor is enabled;
- disabling monitoring clears the monitor microphone queue immediately;
- `VoiceMonitorGain = 0` remains as a second safety layer;
- SoundBoard-to-headphones monitoring stays independent;
- Media Bridge and scene routes are unchanged.

The application log reports:

```text
Voice monitor route: connected
Voice monitor route: hard disconnected
```

## Validation status

No build or automated tests were run. The fix must be validated in the original screen-sharing scenario. If the duplicate remains while the log says `hard disconnected`, the second copy is likely being captured from VoiSee's virtual-output render stream rather than the physical monitor route, and that path will require a separate isolation approach.
