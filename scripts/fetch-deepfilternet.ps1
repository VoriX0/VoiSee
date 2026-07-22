param(
    [Parameter(Mandatory = $false)]
    [string]$DestinationPath = ""
)

$ErrorActionPreference = "Stop"
$releaseVersion = "0.5.6"
$assetName = "deep_filter_ladspa-$releaseVersion-x86_64-pc-windows-msvc.dll"
$downloadUrl = "https://github.com/Rikorose/DeepFilterNet/releases/download/v$releaseVersion/$assetName"

if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $DestinationPath = Join-Path $repoRoot "src\VoiSe.Audio\runtimes\win-x64\native\deep_filter_ladspa.dll"
}

$DestinationPath = [System.IO.Path]::GetFullPath($DestinationPath)
$destinationDirectory = Split-Path -Parent $DestinationPath
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

if (Test-Path $DestinationPath) {
    $existing = Get-Item $DestinationPath
    if ($existing.Length -gt 10000000) {
        Write-Host "DeepFilterNet native library already present: $DestinationPath"
        exit 0
    }

    Remove-Item $DestinationPath -Force
}

$tempPath = "$DestinationPath.download"
Remove-Item $tempPath -Force -ErrorAction SilentlyContinue

Write-Host "Downloading official DeepFilterNet $releaseVersion Windows library..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $tempPath -UseBasicParsing

$downloaded = Get-Item $tempPath
if ($downloaded.Length -le 10000000) {
    Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
    throw "Downloaded DeepFilterNet file is unexpectedly small ($($downloaded.Length) bytes)."
}

$stream = [System.IO.File]::OpenRead($tempPath)
try {
    $first = $stream.ReadByte()
    $second = $stream.ReadByte()
    if ($first -ne 0x4D -or $second -ne 0x5A) {
        throw "Downloaded DeepFilterNet file is not a Windows PE library."
    }
}
finally {
    $stream.Dispose()
}

Move-Item $tempPath $DestinationPath -Force
Write-Host "DeepFilterNet native library saved: $DestinationPath"
