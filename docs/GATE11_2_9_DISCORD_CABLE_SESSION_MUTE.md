# Gate 11.2.9 — Discord CABLE Input Session Mute

## Baseline

VoiSee 11.2.9 is based on 11.2.8 and retains the 11.2.5 audio architecture. The process-isolation experiments from 11.2.6 and 11.2.7 remain excluded.

## Diagnostic service

`AudioDiagnosticsService` now has a stateful, narrowly scoped session-mute operation. It enumerates active render endpoints, selects the normal `CABLE Input` endpoint, then selects only sessions owned by a process named `Discord`.

The service stores the original mute state by endpoint and audio-session instance identifier. While armed, periodic refresh catches replacement sessions created by Discord. Disarming restores stored mute states where the matching session still exists.

## UI

Advanced Settings adds:

- a `Mute Discord session on CABLE Input` checkbox;
- a live target status line;
- an `ARMED` marker in the copied WASAPI report.

The control is intentionally located next to diagnostics rather than normal user settings. It is not persisted.

## Safety boundary

The implementation does not change endpoint volume, endpoint defaults, Discord on headphones, VoiSee's CABLE Input session, or the CABLE Output capture endpoint.

## Lifecycle

The diagnostic mute is restored when:

- the checkbox is unchecked;
- the Advanced Settings dialog closes;
- `AudioDiagnosticsService` is disposed during application shutdown.
