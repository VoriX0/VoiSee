# VoiSee 11.3.0 — Discord screen-share voice isolation

## Confirmed problem

When Discord shared the VoiSee window with sound, the processed voice was delivered twice:

1. through `CABLE Output` as the selected Discord microphone;
2. through Discord's own render session on the VB-CABLE `CABLE Input` endpoint as part of the screen-share audio track.

The VoiSee Voice Monitor route was not the source. Disabling the physical monitor output did not change the duplication, while muting only `Discord → CABLE Input` removed the second copy without disabling the normal virtual microphone.

## Final behavior

VoiSee now keeps the validated isolation enabled automatically whenever the application is running.

The protection targets only render sessions that satisfy both conditions:

- process name is `Discord`;
- playback endpoint is the normal `CABLE Input (VB-Audio Virtual Cable)` endpoint.

It does not modify:

- `VoiSe.App → CABLE Input`;
- Discord sessions on physical headphones or speakers;
- the `CABLE Output` capture endpoint used as the Discord microphone;
- the physical microphone;
- SoundBoard, Voice Changer, Scenes, Media Bridge, or Voice Monitor routing.

VoiSee checks periodically for Discord recreating the screen-share session and reapplies mute when needed. On application exit, VoiSee restores the original mute state of sessions it changed.

## Interface cleanup

The visible interface is returned to the VoiSee 11.2.5 layout.

Removed from Advanced Settings:

- `Mute Discord session on CABLE Input` checkbox;
- `Enable Virtual Mic Output route` checkbox;
- `Enable Monitor Output route` checkbox;
- live WASAPI endpoint/session report;
- snapshot copy button and diagnostic status fields.

The isolation cannot be disabled from the normal user interface.

## Preserved fixes

- Original hard Voice Monitor disconnect behavior from 11.2.5.
- Full-width `Create / Rename / Delete Category` SoundBoard buttons.
- Scene Media Source controls simplified as in 11.2.2.

## Validation status

The underlying session-isolation behavior was confirmed manually by the user in VoiSee 11.2.9: enabling the targeted Discord-on-CABLE mute removed the duplicated voice.

The 11.3.0 source archive was packaged without running a new build or automated tests, following the user's instruction.
