# VoiSee 11.2.9 — Discord CABLE Input session isolation

## Purpose

The 11.2.8 snapshot showed two simultaneous active render sessions on `CABLE Input`:

- VoiSe.App;
- Discord.

Disabling Monitor Output did not change the duplicated voice, while disabling Virtual Mic Output removed both copies. This release adds one precise diagnostic control to determine whether Discord's own render session on CABLE Input is responsible for the screen-share copy.

## Added control

`Settings → Advanced Settings → Discord CABLE Input session isolation` now contains:

- **Mute Discord session on CABLE Input**;
- a live status line with PID, session state, mute state and peak.

The action affects only sessions that satisfy both conditions:

1. render endpoint name starts with `CABLE Input` and is not `CABLE In 16ch`;
2. owning process name is `Discord`.

It does not mute:

- VoiSee's render session on CABLE Input;
- Discord's normal output session on physical headphones;
- the capture endpoint `CABLE Output` used as Discord's microphone;
- the physical microphone;
- the complete VB-CABLE endpoint.

## Temporary and reversible behavior

When the control is enabled, VoiSee records each matched session's original mute state and sets that session to muted. The diagnostic timer re-applies the mute if Discord recreates the session during screen sharing.

When the control is disabled, the Advanced Settings dialog closes, or VoiSee exits, the remembered original mute states are restored on a best-effort basis.

## Interpretation

- duplicated voice disappears but the normal Discord microphone remains audible: Discord's CABLE Input render session is the screen-share duplicate source;
- all voice disappears: that session is also required by Discord's microphone path or routing configuration;
- duplication remains: Discord is directly loopback-capturing VoiSee's CABLE Input session or using another broader capture path.

## Verification status

No build, automated tests, smoke tests, or runtime validation were performed for this package at the user's request.
