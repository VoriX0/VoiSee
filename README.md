# VoiSe Gate 7.7 — Scene single-instance playback and timelines

WinUI 3 starter app for VoiSe with SoundBoard, Voice Changer presets, and Scene Editor.

Gate 7.7 focuses on Scene playback behavior:

- active scene state is visible in the scene list;
- SoundBoard controls are locked while a scene is active;
- looped scene sound has its own timeline and play/pause control;
- every scene sound button has its own seekable timeline;
- each scene sound button can only have one active playback instance;
- repeated clicks restart that button's sound instead of stacking copies;
- different scene buttons can still play together over the looped background;
- scene button volumes remain per-button in the context menu;
- context menu actions are regrouped and include Stop.

See `docs/GATE7_7_SCENE_SINGLE_INSTANCE_TIMELINES.md` for details.
