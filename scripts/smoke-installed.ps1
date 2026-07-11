param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\VoiSee"
)

$Exe = Join-Path $InstallDir "VoiSe.App.exe"

Write-Host "VoiSee install smoke test"
Write-Host "InstallDir: $InstallDir"

if (-not (Test-Path $Exe)) {
    throw "VoiSe.App.exe was not found at $Exe"
}

Write-Host "Found executable." -ForegroundColor Green
Write-Host "Launching VoiSee..."
Start-Process -FilePath $Exe -WorkingDirectory $InstallDir
Write-Host "Check manually: app starts, Engine is Running, SoundBoard plays one sound."
