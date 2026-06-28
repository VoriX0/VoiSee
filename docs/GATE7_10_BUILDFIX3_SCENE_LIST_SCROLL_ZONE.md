# Gate 7.10 buildfix 3 — Scene list scroll zone extension

This buildfix extends only the left scene list wheel routing zone.

## Change

- The left scene list scroll zone now extends downward by 65% of the list height.
- This is an additional +30% over buildfix 2.
- The right Scene sound buttons scroll zone remains a separate X/Y-bounded area.
- The two scene tab wheel zones must not overlap horizontally.

## Files

- `src/VoiSe.App/MainWindow.xaml.cs`
- `VERSION.txt`
