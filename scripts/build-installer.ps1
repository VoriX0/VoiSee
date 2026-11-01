param(
    [ValidateSet("Release", "Debug")][string]$Configuration = "Release",
    [ValidateSet("win-x64")][string]$Runtime = "win-x64",
    [switch]$SkipInstaller,
    [switch]$Portable,
    [switch]$Force,
    [string]$PackageSource = ""
)

$ErrorActionPreference = "Stop"
$ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$Version = (Get-Content -LiteralPath (Join-Path $ProjectRoot "VERSION.txt") -Raw).Trim()
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid release version: $Version" }
$PublishDir = Join-Path $ProjectRoot "artifacts\releases\$Version\app"
$InstallerDir = Join-Path $ProjectRoot "artifacts\installer\$Version"
$Project = Join-Path $ProjectRoot "src\VoiSe.App\VoiSe.App.csproj"
$Iss = Join-Path $ProjectRoot "installer\VoiSe.iss"
if (([xml](Get-Content -LiteralPath $Project -Raw)).Project.PropertyGroup.Version -ne $Version) {
    throw "VERSION.txt and the application project disagree."
}

function Assert-OwnedOutput([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    $allowed = @([IO.Path]::GetFullPath($PublishDir), [IO.Path]::GetFullPath($InstallerDir))
    if ($absolute -notin $allowed -or !$absolute.StartsWith($ProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace a path outside this version's output: $absolute"
    }
    $parent = $absolute
    while ($parent -and $parent -ne $ProjectRoot) {
        if (Test-Path -LiteralPath $parent) {
            if ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing to replace output through a junction or symbolic link: $parent"
            }
        }
        $parent = Split-Path -Parent $parent
    }
}
function Initialize-Output([string]$Path) {
    Assert-OwnedOutput $Path
    if (Test-Path -LiteralPath $Path) {
        if (!$Force) { throw "Output already exists: $Path. Use -Force to replace only this version's output." }
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}
function Find-FullDriverSetup([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return $null }
    foreach ($name in @("VBCABLE_Setup_x64.exe", "VBCABLE_Setup.exe")) {
        foreach ($setup in (Get-ChildItem -LiteralPath $Path -File -Recurse -Filter $name)) {
            $folder = $setup.DirectoryName
            if ((Get-ChildItem -LiteralPath $folder -File -Recurse -Filter '*.inf') -and
                (Get-ChildItem -LiteralPath $folder -File -Recurse -Filter '*.cat') -and
                (Get-ChildItem -LiteralPath $folder -File -Recurse -Filter '*.sys')) { return $setup.FullName }
        }
    }
    return $null
}
function Prepare-DriverBundle {
    $folder = Join-Path $PublishDir 'ThirdParty\VB-CABLE'
    $source = Join-Path $ProjectRoot 'third_party\VB-CABLE'
    if (!(Test-Path -LiteralPath $source) -or
        (!(@(Get-ChildItem -LiteralPath $source -File -Filter '*.zip').Count) -and !(Find-FullDriverSetup $source))) {
        $source = Join-Path $ProjectRoot 'src\VoiSe.App\ThirdParty\VB-CABLE'
    }
    if (!(Test-Path -LiteralPath $source)) { return $false }
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    Get-ChildItem -LiteralPath $source | Copy-Item -Destination $folder -Recurse -Force
    $archives = @(Get-ChildItem -LiteralPath $folder -File -Filter '*.zip')
    if ($archives.Count -gt 1) { throw 'Several VB-CABLE archives found; select a single complete vendor package.' }
    if ($archives.Count -eq 1) {
        $extracted = Join-Path $folder '_extracted'
        Expand-Archive -LiteralPath $archives[0].FullName -DestinationPath $extracted
        if (!(Find-FullDriverSetup $extracted)) { throw 'VB-CABLE archive does not contain a full driver package.' }
        # Keep the entire vendor package, including its notices, in one extracted copy.
        Remove-Item -LiteralPath $archives[0].FullName -Force
    }
    $placeholder = Join-Path $folder 'README_PLACE_VBCABLE_HERE.txt'
    if (Test-Path -LiteralPath $placeholder) { Remove-Item -LiteralPath $placeholder -Force }
    return $null -ne (Find-FullDriverSetup $folder)
}
function Assert-Payload {
    $blockedNames = @('settings.json','soundboard.json','voice-presets.json')
    $files = @(Get-ChildItem -LiteralPath $PublishDir -Recurse -File)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($PublishDir.Length + 1)
        if ($file.Name -in $blockedNames -or $relative -match '(^|\\)(data|sounds|presets|scenes)\\') {
            throw "User data found in the publish payload: $relative"
        }
        if ($file.Name -in @('PresentationFramework.dll','PresentationCore.dll','System.Windows.Forms.dll','System.Drawing.Common.dll','NAudio.WinForms.dll')) {
            throw "Unexpected Windows Desktop dependency: $relative"
        }
    }
    foreach ($line in (Get-Content -LiteralPath (Join-Path $ProjectRoot 'installer\obsolete-desktop-files.iss'))) {
        if ($line -match 'Name: "\{app\}\\([^"]+)"') {
            if (Test-Path -LiteralPath (Join-Path $PublishDir $Matches[1])) { throw "Upgrade cleanup would remove a current payload file: $($Matches[1])" }
        }
    }
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $PublishDir 'VoiSe.App.runtimeconfig.json') -Raw
    if ($runtimeConfig -match 'Microsoft.WindowsDesktop.App') { throw 'The application still requires Windows Desktop.' }
    if (!(Test-Path -LiteralPath (Join-Path $PublishDir 'en\VoiSe.App.resources.dll'))) { throw 'English resources are missing.' }
    foreach ($native in @('deep_filter_ladspa.dll','webrtc-apm.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $PublishDir $native))) { throw "Missing audio processor: $native" }
    }
}

if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET SDK before building.' }
# Validate both paths before any replacement; never touch another release or the source tree.
Assert-OwnedOutput $PublishDir
Assert-OwnedOutput $InstallerDir
if (!$Force -and ((Test-Path -LiteralPath $PublishDir) -or (Test-Path -LiteralPath $InstallerDir))) {
    throw "Version $Version already has build output. Use -Force to replace only that version."
}
Initialize-Output $PublishDir
Initialize-Output $InstallerDir
Write-Host "Publishing VoiSee $Version (self-contained $Runtime)..."
$restoreSource = @()
if ($PackageSource) { $restoreSource = @('--source', $PackageSource) }
& dotnet publish $Project -c $Configuration -r $Runtime --self-contained true `
    -p:WindowsPackageType=None -p:PublishSingleFile=false -p:UseSharedCompilation=false `
    -p:StopRunningVoiSeeBeforeDebugBuild=false -o $PublishDir @restoreSource
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }
$bundled = Prepare-DriverBundle
# Retain development symbols in bin/obj, not in the user installation.
foreach ($name in @('VoiSe.App.pdb','VoiSe.Audio.pdb')) {
    $symbols = Join-Path $PublishDir $name
    if (Test-Path -LiteralPath $symbols) { Remove-Item -LiteralPath $symbols -Force }
}
Assert-Payload

if ($Portable -or $SkipInstaller) {
    $zip = Join-Path $InstallerDir "VoiSee-Portable-$Version-x64.zip"
    Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $zip
    Write-Host "Portable: $zip"
}
$setupPath = Join-Path $InstallerDir "VoiSee-Setup-$Version-x64.exe"
if (!$SkipInstaller) {
    $compiler = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "${env:ProgramFiles}\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$compiler) { throw 'Inno Setup 6 is required to build the installer. Use -SkipInstaller for a portable build.' }
    $arguments = @('/Qp', "/DMyAppVersion=$Version", "/DPublishSource=$PublishDir")
    if ($bundled) { $arguments += '/DVBCABLE_BUNDLED' }
    & $compiler @arguments $Iss
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $setupPath)) { throw 'Installer compilation failed.' }
}
$files = @(Get-ChildItem -LiteralPath $PublishDir -Recurse -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
$commit = (& git -C $ProjectRoot rev-parse HEAD).Trim()
$manifest = [ordered]@{ Version=$Version; Commit=$commit; ApplicationBytes=$bytes; FileCount=$files.Count; BundledVBCable=$bundled }
if (Test-Path -LiteralPath $setupPath) {
    $installer = Get-Item -LiteralPath $setupPath
    $manifest.Installer = $installer.Name
    $manifest.Bytes = $installer.Length
    $manifest.SHA256 = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $InstallerDir 'build-manifest.json') -Encoding UTF8
Write-Host "Application: $([Math]::Round($bytes / 1MB, 2)) MiB, $($files.Count) files."
if (Test-Path -LiteralPath $setupPath) { Write-Host "Installer: $([Math]::Round((Get-Item -LiteralPath $setupPath).Length / 1MB, 2)) MiB -- $setupPath" }
