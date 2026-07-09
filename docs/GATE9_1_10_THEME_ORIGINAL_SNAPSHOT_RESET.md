# Gate 9.1.10 — Theme original snapshot reset

Fixes the remaining theme reset issue after application restart.

## Changes

- Theme engine now keeps a separate original-style snapshot for each visible element.
- Empty CSS values restore the original VoiSee/XAML value instead of the previous active theme value.
- Switching from a saved theme such as Amber_Studio to Empty_Reset_Test should no longer leave ComboBox/ListView/category colors behind.
- Removed dependency on the previous theme snapshot for blank declarations.
- Voice preset tile icons are restored to a larger visible size.

## Test scenario

1. Select Amber_Studio.
2. Close VoiSee.
3. Start VoiSee again.
4. Select Empty_Reset_Test or a newly created blank theme.
5. Check Settings ComboBoxes and SoundBoard category list.
6. Check Voice Changer preset icon size.
