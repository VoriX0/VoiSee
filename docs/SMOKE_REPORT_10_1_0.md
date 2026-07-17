# VoiSee 10.1.0 static smoke report

**Result: 36/36 PASS**

> Full WinUI compilation and runtime theme loading require Windows with the configured .NET/Windows App SDK toolchain.

| # | Check | Result | Detail |
|---:|---|:---:|---|
| 1 | Version: VERSION.txt | PASS | VoiSee Version 10.1.0 |
| 2 | Version: src/VoiSe.App/VoiSe.App.csproj | PASS | <Version>10.1.0</Version> |
| 3 | Version: src/VoiSe.App/MainWindow.xaml | PASS | VoiSee Version 10.1.0 |
| 4 | Version: src/VoiSe.App/MainWindow.xaml.cs | PASS | VoiSee Version 10.1.0 UI started. |
| 5 | Version: installer/VoiSe.iss | PASS | #define MyAppVersion "10.1.0" |
| 6 | Version: scripts/build-installer.ps1 | PASS | $Version = "10.1.0" |
| 7 | XML: src/VoiSe.App/App.xaml | PASS |  |
| 8 | XML: src/VoiSe.App/MainWindow.xaml | PASS |  |
| 9 | XML: src/VoiSe.App/Themes/DefaultDark.xaml | PASS |  |
| 10 | XML: sample-themes/Neon_Cyan.voiseetheme.xaml | PASS |  |
| 11 | Removed CSS runtime: VoiSeeCssTheme | PASS |  |
| 12 | Removed CSS runtime: VoiSeeCssRule | PASS |  |
| 13 | Removed CSS runtime: RuleRegex | PASS |  |
| 14 | Removed CSS runtime: ApplyRulesToElement | PASS |  |
| 15 | Removed CSS runtime: ApplyThemeIncremental | PASS |  |
| 16 | Native theme engine: XamlReader.Load | PASS |  |
| 17 | Native theme engine: Application.Current.Resources.MergedDictionaries | PASS |  |
| 18 | Native theme engine: ValidateSemanticContract | PASS |  |
| 19 | Native theme engine: MigrateLegacyCssThemes | PASS |  |
| 20 | Native theme engine: ReadAllTextWithRetry | PASS |  |
| 21 | Required resources: src/VoiSe.App/Themes/DefaultDark.xaml | PASS |  |
| 22 | Required resources: sample-themes/Neon_Cyan.voiseetheme.xaml | PASS |  |
| 23 | All MainWindow ThemeResource keys have defaults | PASS |  |
| 24 | Theme panel uses XAML terminology | PASS |  |
| 25 | Theme panel CSS selector help removed | PASS |  |
| 26 | All MainWindow XAML handlers exist | PASS |  |
| 27 | New theme extension used | PASS |  |
| 28 | Legacy CSS archive implemented | PASS |  |
| 29 | Invalid XAML keeps previous theme | PASS |  |
| 30 | Tab switch has no visual-tree repaint | PASS |  |
| 31 | CSS sample removed | PASS |  |
| 32 | XAML sample present | PASS |  |
| 33 | User data publish exclusions retained | PASS |  |
| 34 | No user JSON data included | PASS |  |
| 35 | Gate 10.1 documentation present | PASS |  |
| 36 | Full VoiSee 10 specification included | PASS |  |

## Required Windows checks

1. `dotnet clean .\src\VoiSe.App\VoiSe.App.csproj`
2. `dotnet build .\src\VoiSe.App\VoiSe.App.csproj -c Release`
3. Start VoiSee and create a `.voiseetheme.xaml` theme.
4. Verify live reload, invalid-XAML fallback, rename, delete, and rapid tab switching.
5. Verify old CSS files are moved to `themes\Legacy CSS`.
6. Re-run the full 9.2.7 audio/SoundBoard/Scenes/Sound Editor regression checklist.

## Scope note

VoiSee 10.1.0 is based on stable 9.2.7. The unverified 10.0.0 audio-monitor hard-isolation experiment is intentionally not merged into this archive.
