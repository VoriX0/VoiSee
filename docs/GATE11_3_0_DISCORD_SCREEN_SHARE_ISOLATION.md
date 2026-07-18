# Gate 11.3.0 — Discord screen-share voice isolation

## Architecture

The VoiSee audio engine still writes the processed virtual-microphone mix to the VB-CABLE render endpoint:

```text
VoiSee → CABLE Input → CABLE Output → Discord microphone
```

During screen sharing, Discord also creates a render session on `CABLE Input`. That session can place the same processed voice into the screen-share audio track, producing a second copy for viewers.

VoiSee 11.3.0 adds a narrow background service:

```text
DiscordCableSessionIsolationService
  └─ enumerates active render endpoints
      └─ finds normal CABLE Input
          └─ finds process Discord
              └─ mutes only that session
```

## Safety boundaries

Endpoint matching excludes `CABLE In 16ch` and requires the normal friendly-name prefix `CABLE Input`.

Session matching requires the resolved process name to equal `Discord`, case-insensitively.

No endpoint master volume is changed. No VoiSee session is muted. No Discord session on the physical output is changed.

## Lifecycle

- Service is enabled during `MainWindow` construction.
- A 750 ms dispatcher timer reapplies the mute to newly created or recreated Discord sessions.
- Original session mute states are recorded once per session instance.
- On VoiSee shutdown, changed sessions are restored on a best-effort basis.
- The feature has no user-facing switch.

## UI baseline

`MainWindow.xaml` and the Advanced Settings dialog remain based on VoiSee 11.2.5, apart from the displayed version number. Diagnostic controls introduced in 11.2.8 and 11.2.9 are not included.
