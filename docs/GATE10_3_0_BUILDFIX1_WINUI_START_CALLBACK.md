# VoiSee 10.3.0 buildfix 1 — WinUI startup callback compile fix

## Symptom

`dotnet run --project src/VoiSe.App` failed in `Program.cs` with CS0029:

```text
Cannot implicitly convert type "VoiSe.App.App" to
"Microsoft.UI.Xaml.ApplicationInitializationCallbackParams".
```

## Root cause

The `Application.Start` lambda parameter was named `_`:

```csharp
Application.Start(_ =>
{
    _ = new App();
});
```

Inside that lambda `_` was a real parameter of type
`ApplicationInitializationCallbackParams`, not a discard. Therefore
`_ = new App();` attempted to assign a `VoiSe.App.App` object to that callback
parameter.

## Fix

- renamed the callback parameter so it cannot be mistaken for a discard;
- construct `App` directly with `new App();`, matching the normal WinUI 3
  generated startup pattern;
- removed the unused `_themeReapplyGeneration` field that produced CS0169.

The application version remains 10.3.0; this archive is buildfix 1 for that gate.

## Required Windows check

```powershell
dotnet clean .\src\VoiSe.App\VoiSe.App.csproj
dotnet run --project .\src\VoiSe.App
```

Then verify normal launch, close-to-tray, tray restore, second-instance activation,
real Exit, and `--background` startup.
