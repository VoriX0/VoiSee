# Gate 7.10 buildfix 2 — Scene list scroll zone extension

This buildfix adjusts the wheel routing for the Scenes tab.

## Change

- The left scene list keeps its own X-bounded wheel zone.
- The left scene list wheel zone is extended downward by 35% of the scene list height.
- The right Scene sound buttons scroller remains in its own X-bounded zone.
- The previous fix that prevents the scene list and sound button zones from stealing each other's wheel events is preserved.

## Reason

After Gate 7.10 buildfix 1 the scene list and sound button scroll zones no longer overlapped horizontally, but the scene list wheel zone was too short vertically. This made scrolling unreliable near the lower scene controls. Extending only the scene-list zone downward fixes that without reintroducing overlap with the right-side sound buttons area.
