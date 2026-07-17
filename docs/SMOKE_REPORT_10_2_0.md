# Static smoke report — VoiSee 10.2.0

## Result

**40 / 40 PASS**

## Checked

- version 10.2.0 is synchronized in `VERSION.txt`, README, project metadata, XAML, startup log, installer and build script;
- all XAML and project XML files parse successfully;
- all 31 C# files parse successfully with the C# tree-sitter grammar;
- `Show in File Explorer` is present and uses `explorer.exe /select,`;
- missing files produce a visible dialog and log entry;
- context menu contains a `Transfer to Category` submenu;
- submenu exposes separate Move and Copy actions;
- current category is excluded from the target list;
- Move modifies `CategoryId` without copying or moving the physical file;
- Move does not modify `CreatedAtUtc` or `UpdatedAtUtc`;
- Move preserves the existing object ID, hotkey, usage count and scene references;
- Copy creates a new GUID and physical file;
- Copy assigns the target category;
- Copy clears the hotkey and resets usage count;
- Copy does not create scene references;
- copy naming uses `[copy]`, `[copy 2]`, and subsequent suffixes;
- successful operations switch to the target category and select the result;
- all three VoiSee 10.1.5 sample XAML themes remain present;
- no `bin`, `obj`, user sound library, scene or preset directories are included.

## Environment limitation

The container does not provide the Windows App SDK XAML compiler or a Windows audio environment. A real WinUI build and Explorer/UI behavior must still be checked on Windows.

## Windows smoke steps

1. Run `dotnet clean .\src\VoiSe.App\VoiSe.App.csproj`.
2. Run `dotnet build .\src\VoiSe.App\VoiSe.App.csproj -c Release`.
3. Run `dotnet run --project .\src\VoiSe.App`.
4. Create at least two categories and add a track.
5. Right-click the track and run `Show in File Explorer`; verify the correct file is selected.
6. Assign a hotkey and add the track to a scene.
7. Move it to another category; verify the hotkey, usage count and scene button still work.
8. Copy it to another category; verify the copy has a new ID/name and no hotkey.
9. Edit the copied sound and confirm the original audio file is unchanged.
10. Repeat Copy to verify `[copy 2]` naming.
