# VoiSee 12.3.0 buildfix 1 — rawValue compile fix

## Symptom

Windows build failed with:

`MainWindow.xaml.cs(7224,37): error CS0103: The name "rawValue" does not exist in the current context.`

The subsequent WinUI `XamlCompiler.exe` / MSB3073 failure was downstream from the failed compilation step.

## Cause

During the 12.3.0 manual-value-range refactor, the drag preview card in `CreateEffectDragVisual()` was accidentally changed to format `rawValue`. That method only has the parameter `value`; `rawValue` is local to `CreateActiveEffectCard()`.

## Fix

The drag preview text now formats `value` again.

The active effect card still keeps the 12.3.0 behavior:

- manual text input accepts values from -10000 through +10000;
- the slider remains visually clamped to its normal per-effect UI range;
- the raw text value is retained for DSP/settings processing.

Application version remains `12.3.0`; this archive is buildfix 1 of that version.
