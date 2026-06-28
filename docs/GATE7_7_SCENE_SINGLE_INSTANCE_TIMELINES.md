# Gate 7.7 — Scene single-instance playback and timelines

Changes:

- Scene sound buttons now have independent playback timelines under the button title.
- Each timeline shows current time and total duration and can seek the matching scene sound instance.
- The looped sound slot now has its own timeline under the sound plaque and a small play/pause button to the left of that timeline.
- Scene sound playback uses stable playback keys:
  - one key for the looped background sound;
  - one key per scene sound button.
- Pressing the same scene sound button again restarts the same playback instance instead of stacking unlimited copies.
- Different scene sound buttons can still play together over the looped background sound.
- The scene sound button context menu is regrouped into compact rows and includes Stop for the selected button instance.
- SoundBoard controls are locked while a scene is active:
  - SoundBoard play/pause, stop, previous, next are disabled;
  - SoundBoard volume sliders are disabled;
  - SoundBoard timeline ignores scene playback;
  - a centered notice explains that SoundBoard playback is unavailable while a scene is active.
