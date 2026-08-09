# VoiSee 12.3.3 buildfix 1

## XAML compiler stabilization

- Kept the 12.3.3 native collection scrolling architecture:
  - SoundBoard sounds: ListView
  - Voice presets: ListView
  - Processing chain: ScrollView
  - Effect library: ListView
  - Saved scenes: ListView
  - Scene sounds: ListView
  - Settings: ScrollView
- Removed the large static `ListView.Items` tree from Effect Library XAML.
- Effect Library sections/cards are now created in `MainWindow.xaml.cs` after `InitializeComponent()`.
- Existing hidden effect slider/text-box compatibility controls remain in XAML, but outside the ListView item collection.
- Removed the unused `_activeSoundEditorScrollViewer` field and the associated warning path.

## Why

Windows App SDK 1.6 standalone XamlCompiler can return only MSB3073 / exit code 1 without printing the underlying XAML diagnostic. The previous 12.3.3 build compiled once for the tester, then failed on a second unchanged `dotnet run`, pointing to an incremental XAML compiler/state issue rather than a normal C# compile failure. This buildfix reduces the generated XAML namescope and static item tree substantially.
