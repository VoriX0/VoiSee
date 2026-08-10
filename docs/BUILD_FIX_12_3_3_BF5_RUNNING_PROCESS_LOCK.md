# VoiSee 12.3.3 buildfix 5 — running process build lock

The Debug build now stops an already-running `VoiSe.App.exe` before `PrepareForBuild`.
This prevents MSBuild copy errors MSB3026/MSB3027/MSB3021 when the previous application instance still holds `VoiSe.Audio.dll`.
The target runs only on Windows Debug builds and can be disabled with:

`dotnet run --project src/VoiSe.App -p:StopRunningVoiSeeBeforeDebugBuild=false`

Release/publish builds are not affected.
