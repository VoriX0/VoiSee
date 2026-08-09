# VoiSee 12.3.3 buildfix 2

Build-system stabilization for the collection/native-scrolling migration.

- Restores the pre-12.3.3 XAML field types for legacy x:Name identifiers as collapsed compatibility elements.
- Gives new ListView controls fresh x:Name identifiers, avoiding incremental generated-field type replacement.
- Keeps ScrollView for Processing Chain and Settings; Windows App SDK 1.6 supports ScrollView.
- Uses an isolated BaseIntermediateOutputPath so normal `dotnet run` does not reuse stale XAML compiler state from previous 12.3.x layouts.
- No user data is added to the archive.
