# VoiSee regression harness

Windows-only console regression tests for the audio transport and persistence commit boundary. No microphone, render devices, user profile or drivers are opened. Test WAV/JSON files are created below the repository's `artifacts/regression-tests/<guid>` and removed after success. Failures leave fixtures for inspection.

Run from the repository root:

```powershell
dotnet run --project tests/VoiSe.RegressionTests -c Release
```

Expected: `REGRESSION_TESTS=PASS (14)`. No external test framework is required. The project references production audio code and links the actual application storage classes with a console-only StartupLog adapter.

For an offline machine with the dependencies already cached:

```powershell
dotnet restore tests/VoiSe.RegressionTests --source "$env:USERPROFILE\.nuget\packages"
dotnet run --project tests/VoiSe.RegressionTests -c Release --no-restore -p:UseSharedCompilation=false
```
