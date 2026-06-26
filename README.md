# VoiSe Gate 5.26 — SoundBoard Overlay Sound Scroller

Gate 5.26 keeps the current SoundBoard design from Gate 5.25, but replaces the problematic native ListView scrolling area with a custom overlay-style sound scroller.

## What changed

- Window/header version updated to Gate 5.26.
- The current SoundBoard visual layout is preserved:
  - categories on the left;
  - small gap;
  - Sounds area on the right;
  - header/search/Add/Delete layout unchanged.
- The old scrollable sound ListView was removed from the right pane.
- A new independent `ScrollViewer + StackPanel` sound list is rendered in the same visual position.
- The new sound rows keep:
  - selection;
  - right-click context menu;
  - double-click to play;
  - search filtering;
  - Add/Delete sound flow.

## Run

```powershell
dotnet run --project src/VoiSe.App
```

## Check

1. The window title says Gate 5.26.
2. The SoundBoard design should look the same as Gate 5.25.
3. Try mouse wheel scrolling directly over the Sounds list.
4. Double-click a sound row to play it.
5. Right-click a sound row and check the context menu.
