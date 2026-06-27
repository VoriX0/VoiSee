# VoiSe Gate 7.4 — Scene Loop Slot Polish

Gate 7 starts the Scenes feature.

A scene now captures and restores:

- current Voice Changer slider values;
- last applied voice preset name when available;
- Voice Monitor On/Off;
- selected SoundBoard category;
- selected SoundBoard sound;
- Virtual Mic Master;
- SoundBoard → Virtual Mic volume;
- SoundBoard → Headphones volume;
- SoundBoard virtual mic delay.

Scenes are stored as separate JSON files in:

```text
%LOCALAPPDATA%\VoiSe\scenes\
```

Run:

```powershell
dotnet run --project src/VoiSe.App
```


## Gate 7.1

See `docs/GATE7_1_SCENE_EDITOR_REDESIGN.md` for the scene editor redesign details.


## Gate 7.4

- Raised the scene control buttons in the left scene menu.
- Reworked the looped sound area as a non-button slot with `No sound` placeholder.
- Added equal-height loop action icon buttons and `⮏` choose/replace icon.
- Moved Voice Monitor into the Clear/Create new row.
- Added SoundBoard overlay playback so normal scene sounds can play over an active loop.
