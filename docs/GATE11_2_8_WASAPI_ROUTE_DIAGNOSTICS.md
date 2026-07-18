# Gate 11.2.8 — WASAPI Route Diagnostics

## Baseline

VoiSee 11.2.8 is based on 11.2.5. It deliberately excludes the detached-output experiment from 11.2.6 and the full AudioHost experiment from 11.2.7 because neither changed the reported Discord behavior.

## Audio engine additions

`Gate2UnifiedAudioEngine` now exposes read-only route metadata and playback states:

- input endpoint ID and name;
- virtual output endpoint ID and name;
- monitor endpoint ID and name;
- virtual-output route enabled state;
- monitor-output route enabled state;
- `WasapiOut.PlaybackState` for both outputs.

It also adds two session-only hard switches:

- `SetVirtualOutputRouteEnabled(bool)`;
- `SetMonitorOutputRouteEnabled(bool)`.

Each disabled route stops its own `WasapiOut`. Microphone samples are no longer queued to the virtual microphone queue while the virtual route is disabled, and queues are cleared when route state changes to prevent delayed voice bursts when the route is restored.

## Read-only Core Audio snapshot

`AudioDiagnosticsService` enumerates active render and capture endpoints through `MMDeviceEnumerator`. For render endpoints it reads `AudioSessionManager.Sessions` and records process/session metadata and meters. The service does not modify system endpoint volume, mute, defaults or session state.

## UI placement

The diagnostics are added to the existing **Advanced Settings** dialog rather than the normal Settings page. This keeps invasive troubleshooting controls out of the everyday interface.

The report refresh timer runs only while the dialog is open and stops when it closes.

## Persistence

Diagnostic output-route switches are intentionally not stored in `settings.json`. Starting or restarting the engine restores both configured outputs.
