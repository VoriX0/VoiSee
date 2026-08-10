$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot

Write-Host 'Stopping any running VoiSe.App instance...' -ForegroundColor Cyan
Get-Process -Name 'VoiSe.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 250

Write-Host 'Building and starting VoiSee...' -ForegroundColor Cyan
dotnet run --project (Join-Path $projectRoot 'src\VoiSe.App\VoiSe.App.csproj')
