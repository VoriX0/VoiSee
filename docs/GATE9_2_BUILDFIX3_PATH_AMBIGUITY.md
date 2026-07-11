# GATE 9.2 Buildfix 3 — Path ambiguity compile fix

## Problem
The timeline editor added `using Microsoft.UI.Xaml.Shapes;` so the identifier `Path` became ambiguous between:

- `System.IO.Path`
- `Microsoft.UI.Xaml.Shapes.Path`

This caused compile errors across `MainWindow.xaml.cs`, including code unrelated to the Sound Editor.

## Fix
- Removed the broad `Microsoft.UI.Xaml.Shapes` namespace import.
- Added narrow aliases only for the two shape classes used by the waveform renderer:
  - `XamlLine`
  - `XamlRectangle`
- Updated the waveform renderer to use those aliases.

The existing `System.IO.Path` references remain unchanged and unambiguous.
