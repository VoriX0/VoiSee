param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$Version = "10.1.0"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$PublishDir = Join-Path $Root "artifacts\publish\VoiSe"
$InstallerDir = Join-Path $Root "artifacts\installer"
$Project = Join-Path $Root "src\VoiSe.App\VoiSe.App.csproj"
$Iss = Join-Path $Root "installer\VoiSe.iss"

function Remove-IfExists([string]$Path) {
    if (Test-Path $Path) {
        Remove-Item $Path -Recurse -Force
    }
}

function Find-VBCableSetup([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }

    $preferred = @(
        "VBCABLE_Setup_x64.exe",
        "VBCABLE_Setup.exe"
    )

    foreach ($name in $preferred) {
        $match = Get-ChildItem -Path $Path -Recurse -Force -File -Filter $name -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($match) { return $match.FullName }
    }

    $match = Get-ChildItem -Path $Path -Recurse -Force -File -Filter "*Setup*x64*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($match) { return $match.FullName }

    $match = Get-ChildItem -Path $Path -Recurse -Force -File -Filter "*Setup*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($match) { return $match.FullName }

    return $null
}

function Prepare-VBCableSourceBundle([string]$Root) {
    $RepoBundleDir = Join-Path $Root "third_party\VB-CABLE"
    $AppBundleDir = Join-Path $Root "src\VoiSe.App\ThirdParty\VB-CABLE"

    if (Test-Path $RepoBundleDir) {
        New-Item -ItemType Directory -Force -Path $AppBundleDir | Out-Null
        Copy-Item -Path (Join-Path $RepoBundleDir "*") -Destination $AppBundleDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Prepare-VBCablePublishBundle([string]$PublishDir) {
    $BundleDir = Join-Path $PublishDir "ThirdParty\VB-CABLE"
    if (-not (Test-Path $BundleDir)) { return $false }

    # Important: VB-CABLE setup must be launched from a full unzipped package folder.
    # A copied VBCABLE_Setup_x64.exe without INF/CAT/SYS next to it shows:
    # "Missing 'inf' file or Driver package corrupted".
    $zip = Get-ChildItem -Path $BundleDir -Force -File -Filter "*.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($zip) {
        $ExtractDir = Join-Path $BundleDir "_extracted"
        Remove-IfExists $ExtractDir
        New-Item -ItemType Directory -Force -Path $ExtractDir | Out-Null
        Write-Host "Extracting bundled VB-CABLE archive for installer checkbox: $($zip.FullName)" -ForegroundColor Cyan
        Expand-Archive -Path $zip.FullName -DestinationPath $ExtractDir -Force

        $setup = Find-VBCableSetup $ExtractDir
        if ($setup) {
            Write-Host "VB-CABLE setup detected in full extracted package: $setup" -ForegroundColor Green
            return $true
        }
    }

    # Fallback for a developer who manually placed the whole extracted VB-CABLE package
    # into third_party\VB-CABLE rather than putting the original ZIP there.
    $setup = Find-VBCableSetup $BundleDir
    if ($setup) {
        $setupDir = Split-Path $setup -Parent
        $hasInf = Get-ChildItem -Path $setupDir -Recurse -Force -File -Filter "*.inf" -ErrorAction SilentlyContinue | Select-Object -First 1
        $hasCat = Get-ChildItem -Path $setupDir -Recurse -Force -File -Filter "*.cat" -ErrorAction SilentlyContinue | Select-Object -First 1
        $hasSys = Get-ChildItem -Path $setupDir -Recurse -Force -File -Filter "*.sys" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($hasInf -and $hasCat -and $hasSys) {
            Write-Host "VB-CABLE setup detected in full package folder: $setup" -ForegroundColor Green
            return $true
        }

        Write-Host "VB-CABLE setup EXE was found, but INF/CAT/SYS package files were not found next to it. It will not be bundled." -ForegroundColor Yellow
    }

    Write-Host "VB-CABLE bundle was not found. Installer will be built without the optional VB-CABLE checkbox." -ForegroundColor Yellow
    return $false
}

function Assert-NoUserDataInPublish([string]$Path) {
    $blockedFileNames = @(
        "settings.json",
        "soundboard.json",
        "voice-presets.json"
    )

    $blockedDirectories = @(
        "data",
        "sounds",
        "presets",
        "scenes"
    )

    foreach ($name in $blockedFileNames) {
        Get-ChildItem -Path $Path -Recurse -Force -File -Filter $name -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }

    foreach ($dir in $blockedDirectories) {
        Get-ChildItem -Path $Path -Recurse -Force -Directory -Filter $dir -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force
    }

    $remaining = @()
    foreach ($name in $blockedFileNames) {
        $remaining += Get-ChildItem -Path $Path -Recurse -Force -File -Filter $name -ErrorAction SilentlyContinue
    }
    foreach ($dir in $blockedDirectories) {
        $remaining += Get-ChildItem -Path $Path -Recurse -Force -Directory -Filter $dir -ErrorAction SilentlyContinue
    }

    if ($remaining.Count -gt 0) {
        $list = ($remaining | Select-Object -ExpandProperty FullName) -join "`n"
        throw "Publish payload still contains user-generated data and will not be packed:`n$list"
    }
}

Write-Host "== VoiSee release build ==" -ForegroundColor Cyan
Write-Host "Root: $Root"
Write-Host "Version: $Version"
Write-Host "Configuration: $Configuration"
Write-Host "Runtime: $Runtime"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK was not found. Install .NET 8 SDK first."
}

Remove-IfExists $PublishDir
Remove-IfExists $InstallerDir

New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $InstallerDir | Out-Null

Prepare-VBCableSourceBundle $Root

Write-Host "Publishing unpackaged self-contained WinUI app..." -ForegroundColor Cyan
dotnet publish $Project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:WindowsPackageType=None `
    -p:PublishSingleFile=false `
    -p:EnableCompressionInSingleFile=false `
    -p:Version=$Version `
    -p:AssemblyVersion=10.1.0.0 `
    -p:FileVersion=10.1.0.0 `
    -p:InformationalVersion=$Version `
    -o $PublishDir

$Exe = Join-Path $PublishDir "VoiSe.App.exe"
if (-not (Test-Path $Exe)) {
    throw "Publish did not produce VoiSe.App.exe at $Exe"
}

Write-Host "Sanitizing publish payload: user categories, presets, scenes, settings, and sounds are excluded." -ForegroundColor Cyan
Assert-NoUserDataInPublish $PublishDir

$VBCableBundled = Prepare-VBCablePublishBundle $PublishDir

Write-Host "Published to: $PublishDir" -ForegroundColor Green

$PortableZip = Join-Path $InstallerDir "VoiSee-Portable-$Version-x64.zip"
Remove-IfExists $PortableZip

Write-Host "Creating portable ZIP..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $PortableZip -Force
Write-Host "Portable ZIP: $PortableZip" -ForegroundColor Green

if ($SkipInstaller) {
    Write-Host "Skipping installer build because -SkipInstaller was specified." -ForegroundColor Yellow
    exit 0
}

$CandidateISCC = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $CandidateISCC) {
    Write-Host "Inno Setup 6 was not found. Install it, then rerun this script." -ForegroundColor Yellow
    Write-Host "Portable ZIP was still created successfully." -ForegroundColor Yellow
    exit 0
}

Write-Host "Building installer with Inno Setup..." -ForegroundColor Cyan
if ($VBCableBundled) {
    & $CandidateISCC "/DVBCABLE_BUNDLED" $Iss
} else {
    & $CandidateISCC $Iss
}

$SetupExe = Join-Path $InstallerDir "VoiSee-Setup-$Version-x64.exe"
if (Test-Path $SetupExe) {
    Write-Host "Installer: $SetupExe" -ForegroundColor Green
} else {
    throw "Installer build finished, but expected setup file was not found: $SetupExe"
}
