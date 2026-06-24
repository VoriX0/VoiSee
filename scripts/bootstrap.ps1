$ErrorActionPreference = "Stop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found. Install .NET SDK 8 or newer."
}

if (-not (Test-Path "VoiSe.sln")) {
    dotnet new sln -n VoiSe
}

dotnet sln VoiSe.sln add src/VoiSe.Audio/VoiSe.Audio.csproj

dotnet sln VoiSe.sln add src/VoiSe.Gate0.Cli/VoiSe.Gate0.Cli.csproj

Write-Host "Restoring packages..."
dotnet restore

Write-Host "Building Gate 0..."
dotnet build src/VoiSe.Gate0.Cli/VoiSe.Gate0.Cli.csproj -c Debug

Write-Host "Done. Try: dotnet run --project src/VoiSe.Gate0.Cli -- --list-devices"
