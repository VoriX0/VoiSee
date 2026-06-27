# Gate 7.1 build fix

Fixes WinUI XamlCompiler failure from the first Gate 7.1 package.

## Change

Removed `Padding` from `VariableSizedWrapGrid` in the Scenes sound button panel. `VariableSizedWrapGrid` does not support this property in the WinUI compiler configuration used by Microsoft.WindowsAppSDK 1.6, so XamlCompiler exited with code 1 before C# compilation.

## Preserved behavior

- Scene list on the left remains unchanged.
- Voice preset picker, Clear, and Create new remain in the scene editor.
- Looped sounds and the autostart checkbox remain in the scene editor.
- Normal scene sound buttons and the `+` add button keep the same rectangular shape and size.
