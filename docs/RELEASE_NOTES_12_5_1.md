# VoiSee 12.5.1 — final theme refresh fix

- Fixes stale user-theme styling on dynamically created Voice Presets and Processing Chain cards after switching back to Default Dark without restarting.
- Effect Library is refreshed through the same path for consistency.
- Theme changes now recreate only dynamic theme-aware visuals after the resource dictionary swap; preset data, effect order, values, bypass state, and selected preset are preserved.
- The same refresh runs for user-theme switches and live theme-file reloads.
- Keeps all 12.5 advanced effects and the 12.4 DPI/scroll/SoundBoard/Scenes fixes.
