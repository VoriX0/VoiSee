# Gate 6.6 — Centered SoundBoard Wheel Zone

Goal: make the SoundBoard wheel catch-zone easier to calibrate by centering it on the whole application window instead of tying it to the visually shifted Sounds list.

Changes:

- The wheel catch-zone is centered on the client area of the application window.
- The zone is oversized and clipped so it does not start above the SoundBoard tab content area.
- When the SoundBoard tab is active, scrolling anywhere below the tab selector scrolls only the Sounds list.
- Voice Changer / Pitch changes from Gate 6.5 are preserved.
