$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$app = Join-Path $projectRoot 'src\VoiSe.App'
$audio = Join-Path $projectRoot 'src\VoiSe.Audio'

Remove-Item -Recurse -Force (Join-Path $app 'bin') -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $app 'obj') -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $audio 'bin') -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $audio 'obj') -ErrorAction SilentlyContinue

dotnet run --project (Join-Path $app 'VoiSe.App.csproj')
