# Gate 9.1.12 — Theme fast tab switching

Goal: remove visible Default Dark flash and UI stall while switching tabs with a custom theme.

Changes:
- Display version: `VoiSee Version 9.1.12`.
- Full theme repaint is still used when the selected theme changes or when the active CSS file is saved.
- Tab switching now uses an incremental theme pass that does not restore/repaint the whole visual tree.
- Removed the previous delayed full-reapply loop `40/160/420 ms` on tab switch.
- Added one lightweight deferred incremental pass after `16 ms` to catch late WinUI template materialization.
- The duplicate tab-selection path no longer calls full theme apply.

Expected result:
- no half-second original-design flash when switching tabs;
- less UI freeze during frequent tab switching;
- custom styles remain applied on SoundBoard, Voice Changer, Scenes, and Settings.
