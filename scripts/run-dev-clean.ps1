$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$projects = @(
    (Join-Path $projectRoot 'src\VoiSe.App'),
    (Join-Path $projectRoot 'src\VoiSe.Audio'),
    (Join-Path $projectRoot 'src\VoiSe.Gate0.Cli')
)

Write-Host 'Cleaning VoiSee build artifacts...' -ForegroundColor Cyan
foreach ($project in $projects) {
    Remove-Item -Recurse -Force (Join-Path $project 'bin') -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $project 'obj') -ErrorAction SilentlyContinue
}

Write-Host 'Starting VoiSee with a fresh MSBuild/XAML intermediate state...' -ForegroundColor Cyan
dotnet run --project (Join-Path $projectRoot 'src\VoiSe.App\VoiSe.App.csproj')
