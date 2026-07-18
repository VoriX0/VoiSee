# VoiSee 11.2.5 — Voice Monitor rollback and diagnostic baseline

## Restored behavior

The experimental gain-only Voice Monitor behavior from 11.2.4 has been rolled back.

Voice Monitor now again uses the pre-11.2.4 hard route behavior:

- **Voice Monitor Off**: processed microphone samples are not queued to the physical monitor route;
- the monitor microphone queue is cleared when the route is disabled;
- **Voice Monitor On**: the processed microphone route is connected again;
- `VoiceMonitorGain` remains a second safety layer;
- SoundBoard headphone monitoring remains independent.

The log reports either:

- `Voice monitor route: connected`
- `Voice monitor route: hard disconnected`

## Preserved unrelated fix

The SoundBoard category buttons remain fixed:

- Create;
- Rename;
- Delete.

They occupy the complete row in three equal-width columns.

## Screen-share diagnosis

No new speculative screen-share audio workaround is included in this build.
The most likely remaining direction is capture of another VoiSee render stream,
especially the virtual microphone output stream, by application-audio screen sharing.
This requires a separate diagnostic step before changing the audio architecture.

## Verification status

Builds, automated tests, XML checks, and smoke tests were not run at the user's request.
