# Gate 6.9 — Voice and Settings Scroll Fix

Keeps the working Gate 6.8 SoundBoard wheel catch-zone and applies the same low-level wheel-routing style to Voice Changer and Settings logs.

Changes:

- Voice Changer wheel zone extends from the tab content top to the bottom of the window.
- Settings log wheel zone extends from the log area top to the bottom of the window.
- Settings log uses the TextBox internal ScrollViewer through visual-tree lookup for manual wheel routing.
- SoundBoard scroll logic is intentionally unchanged from Gate 6.8.
