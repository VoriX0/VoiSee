param([string]$DestinationPath = "")
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
    $DestinationPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/VoiSe.Audio/runtimes/win-x64/native/webrtc-apm.dll'
}
$DestinationPath = [IO.Path]::GetFullPath($DestinationPath)
$nativeHash = '89AB213150E0A3F7D0CA6424EF8F72E2402ADABCFAE8A369976B8B27DF207411'
if ((Test-Path -LiteralPath $DestinationPath) -and (Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256).Hash -eq $nativeHash) {
    Write-Host 'Pinned WebRTC APM 1.4.0 library verified.'
    exit 0
}
$nativeDirectory = Split-Path -Parent $DestinationPath
New-Item -ItemType Directory -Path $nativeDirectory -Force | Out-Null
$packagePath = Join-Path $nativeDirectory (([Guid]::NewGuid().ToString('N')) + '.nupkg')
$nativeTempPath = "$packagePath.dll"
try {
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/soundflow.extensions.webrtc.apm/1.4.0/soundflow.extensions.webrtc.apm.1.4.0.nupkg' -OutFile $packagePath
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ne 'B7B5D54EC40187B85919801F05530F5B178EEAFA18227643B6757394D6854EAF') { throw 'Package hash mismatch.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entry = $archive.GetEntry('runtimes/win-x64/native/webrtc-apm.dll')
        if ($null -eq $entry) { throw 'Windows x64 library missing.' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $nativeTempPath)
    } finally { $archive.Dispose() }
    if ((Get-FileHash -LiteralPath $nativeTempPath -Algorithm SHA256).Hash -ne $nativeHash) { throw 'Native library hash mismatch.' }
    Copy-Item -LiteralPath $nativeTempPath -Destination $DestinationPath -Force
} finally {
    Remove-Item -LiteralPath $packagePath,$nativeTempPath -Force -ErrorAction SilentlyContinue
}
