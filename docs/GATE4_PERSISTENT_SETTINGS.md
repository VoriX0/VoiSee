# Gate 4 — Persistent Settings

Gate 4 turns the WinUI 3 control panel into a reusable daily test application by saving and restoring the most important user settings.

## Saved settings

- Input microphone ID and friendly name.
- Virtual output device ID and friendly name.
- Monitor output device ID and friendly name.
- Last selected sound file path.
- Virtual Mic Master volume.
- Voice Monitor state.
- SoundBoard virtual mic volume.
- SoundBoard headphones volume.
- SoundBoard virtual mic delay.
- Voice Gain.
- Gate Threshold.
- Compressor Threshold.

## Storage

Settings are stored as JSON:

```text
%LOCALAPPDATA%/VoiSe/settings.json
```

The file has `schemaVersion = 1`. Later gates can introduce migration logic when the settings model changes.

## Device restore strategy

Device restore first tries exact device ID. If the device ID is no longer available, the app tries the saved friendly name. If that also fails, it falls back to useful defaults such as `Fifine`, `CABLE Input`, and `Realtek`.

This allows the app to survive common device reconnect scenarios without crashing.

## Acceptance checks

1. Settings file is created after first run.
2. Selected devices are restored after restart.
3. Last selected sound file is restored after restart.
4. SoundBoard Virtual Mic Delay remains 85 ms unless changed.
5. SoundBoard route volumes are restored.
6. Voice Monitor On/Off is restored.
7. Voice Changer slider values are restored.
