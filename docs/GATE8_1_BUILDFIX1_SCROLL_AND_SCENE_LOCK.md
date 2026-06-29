# Gate 8.1 buildfix 1 — log wheel zones and scene SoundBoard lock

Changes:

- Fixed the application log dialog wheel handling using the same modal wheel routing pattern as the Voice Changer preset icon picker.
- The log dialog wheel zone now extends downward and leftward, so wheel scrolling works across the useful modal area instead of only directly above the text.
- Extended the Voice Changer preset icon picker wheel zone 50% to the left while preserving the existing downward expansion.
- While a scene is active, the SoundBoard Loop toggle is now disabled together with Play/Pause, Stop, Previous, Next, timeline, and SoundBoard volume sliders.
- Added a guard so the SoundBoard Loop toggle cannot change the SoundBoard loop state while a scene is active.
