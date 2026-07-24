# Gate 12.2 — Effect Cards UI Prototype

## Goal

Validate the visual organization of the Voice Changer Effects area before introducing a new ordered effect-chain data model.

## Chosen composition

- Left processing line: 36% width.
- Right effect library: 64% width.
- Vertical signal metaphor from microphone to speaker.
- Existing controls are retained with their original x:Name and event handlers, but are now presented as cards.

## Deliberately deferred

- drag-and-drop;
- insertion markers;
- true runtime effect reordering;
- adding and removing effect instances;
- live library preview;
- preset migration to an ordered effect list.

These behaviors should be implemented only after the visual prototype is approved.
