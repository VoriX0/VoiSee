# VoiSee 12.3.3 buildfix 3 — MSBuild obj duplicate attributes

## Symptom

`dotnet run --project src/VoiSe.App` failed in `VoiSe.Audio` with CS0579 duplicate attributes coming from both:

- `obj/Release/net8.0-windows/...AssemblyInfo.cs`
- `obj/voisee_12_3_3_bf2/Debug/net8.0-windows/...AssemblyInfo.cs`

## Root cause

Buildfix 2 set `BaseIntermediateOutputPath` in root `Directory.Build.props` to
`obj\voisee_12_3_3_bf2\`.

The .NET SDK normally excludes its active intermediate directory from wildcard
`Compile` discovery. Changing that active directory meant old generated files
under the previous `obj/Release/...` tree were no longer covered by the active
intermediate-path exclusion and could be compiled together with newly generated
files. Both sets contain assembly-level attributes, hence CS0579.

## Fix

- restore the standard SDK `obj\` intermediate layout;
- keep a neutral `Directory.Build.props` so extracting this buildfix over bf2
  overwrites the broken property;
- `scripts/run-dev-clean.ps1` removes `bin/obj` for App, Audio and Gate0 CLI
  before running the app.

No application/runtime logic was changed by this buildfix.
