# Static smoke report — VoiSee 10.3.0 buildfix 1

## Result

**18 / 18 checks PASS**

This report validates the source archive statically. A real WinUI 3 compilation and runtime test still require Windows with .NET 8 and Windows App SDK 1.6.

## Checks

1. PASS — All XML/XAML/project/template files parse.
2. PASS — All 34 C# files parse with tree-sitter-c-sharp.
3. PASS — Application.Start callback parameter is not named underscore.
4. PASS — App is constructed directly without assignment to callback parameter.
5. PASS — Original CS0029-producing pattern is absent.
6. PASS — Unused _themeReapplyGeneration field removed.
7. PASS — Custom generated Main remains enabled.
8. PASS — Program.Main remains STAThread.
9. PASS — COM wrappers initialize before Application.Start.
10. PASS — DispatcherQueue synchronization context is installed.
11. PASS — Single-instance gate remains before WinUI startup.
12. PASS — Background argument handling remains.
13. PASS — All 61 XAML On* handlers resolve.
14. PASS — Version remains VoiSee 10.3.0.
15. PASS — No bin/obj files are packaged.
16. PASS — No user runtime settings/library files are packaged.
17. PASS — Buildfix implementation note included.
18. PASS — README records buildfix.

## Corrected compiler failure

- The `Application.Start` callback parameter no longer shadows the discard identifier `_`.
- `new App();` is now a standalone object-creation statement.
- The CS0029-producing `_ = new App();` expression is absent.
- The unused `_themeReapplyGeneration` field was removed.

## Required Windows verification

```powershell
dotnet clean .\src\VoiSe.App\VoiSe.App.csproj
dotnet run --project .\src\VoiSe.App
```

After startup, verify close-to-tray, tray restore, second-instance activation, `Exit VoiSee`, and `--background`.

## Environment limitation

The current environment does not contain `dotnet`, MSBuild, Windows App SDK, or Windows. Therefore this report does not claim a successful WinUI compilation or runtime test.
