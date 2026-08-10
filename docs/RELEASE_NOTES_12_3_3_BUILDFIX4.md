# VoiSee 12.3.3 buildfix 4

Fixes WinUI XAML compiler failure introduced by the ScrollView migration.

Root cause: `ScrollView.HorizontalScrollBarVisibility` uses the `ScrollingScrollBarVisibility` enum (`Auto`, `Visible`, `Hidden`). `Disabled` is valid for `ScrollViewer.ScrollBarVisibility`, but invalid for `ScrollView`. The Processing Chain and Settings ScrollView controls used `HorizontalScrollBarVisibility="Disabled"`, causing `XamlCompiler.exe` to exit with code 1.

Fix:
- ProcessingChainScrollView: `HorizontalScrollBarVisibility="Hidden"`; `HorizontalScrollMode="Disabled"` remains.
- SettingsScrollView: `HorizontalScrollBarVisibility="Hidden"`; `HorizontalScrollMode="Disabled"` remains.
