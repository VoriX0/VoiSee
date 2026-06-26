# Gate 6.10 — Extended Voice and Settings Scroll

This gate keeps the working SoundBoard scroll logic from Gate 6.8 / Gate 6.5 and applies the same key idea to Voice Changer and Settings logs.

## Root cause

SoundBoard worked because its wheel hit zone was extended beyond `RootGrid.ActualHeight`, compensating for the fullscreen coordinate mismatch. Voice Changer and Settings were clipped at `RootGrid.ActualHeight`, so the lower part of the maximized window became a dead scroll zone.

## Fix

Voice Changer and Settings logs now use `IsPointInExtendedVerticalWheelZone(...)`, which starts from the relevant content top and extends the bottom zone in the same style as SoundBoard.
