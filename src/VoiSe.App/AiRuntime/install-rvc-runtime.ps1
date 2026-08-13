param(
    [string]$RuntimeRoot = "$env:LOCALAPPDATA\VoiSe\AI Runtime\rvc"
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$env:PIP_DISABLE_PIP_VERSION_CHECK = '1'
$PinnedCommit = '81eed5e8f68b6bed1789f682fe78cdd324495afc'
$RepoArchive = "https://github.com/RVC-Project/Retrieval-based-Voice-Conversion-WebUI/archive/$PinnedCommit.zip"
$HubRepo = 'lj1995/VoiceConversionWebUI'

function Write-Step([string]$Text) {
    Write-Host "`n=== $Text ===" -ForegroundColor Cyan
}

function New-VoiSeeRequirements([string]$SourcePath) {
    if (-not (Test-Path $SourcePath)) { throw "Requirements file not found: $SourcePath" }
    $target = Join-Path $env:TEMP ("voisee-rvc-requirements-" + [guid]::NewGuid().ToString('N') + '.txt')
    Get-Content $SourcePath | Where-Object { $_ -notmatch '^\s*--(index-url|extra-index-url)\b' } | Set-Content -Path $target -Encoding UTF8
    return $target
}

function Resolve-Python312 {
    $candidates = @()
    try {
        $py = & py -3.12 -c "import sys; print(sys.executable)" 2>$null
        if ($LASTEXITCODE -eq 0 -and $py) { $candidates += $py.Trim() }
    } catch {}

    $userPython = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'
    if (Test-Path $userPython) { $candidates += $userPython }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path $candidate) { return $candidate }
    }

    if (Get-Command winget.exe -ErrorAction SilentlyContinue) {
        Write-Step 'Installing Python 3.12 for the current user'
        & winget.exe install --id Python.Python.3.12 --exact --scope user --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) {
            throw "winget could not install Python 3.12 (exit code $LASTEXITCODE)."
        }
        Start-Sleep -Seconds 2
        if (Test-Path $userPython) { return $userPython }
        try {
            $py = & py -3.12 -c "import sys; print(sys.executable)" 2>$null
            if ($LASTEXITCODE -eq 0 -and $py -and (Test-Path $py.Trim())) { return $py.Trim() }
        } catch {}
    }

    throw 'Python 3.12 x64 was not found and could not be installed automatically. Install Python 3.12 x64, then run this installer again.'
}

function Get-NvidiaProfile {
    $gpus = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    $nvidia = $gpus | Where-Object { $_ -match 'NVIDIA' } | Select-Object -First 1
    if (-not $nvidia) { return @{ Kind = 'cpu'; Name = ($gpus -join ', ') } }
    if ($nvidia -match 'RTX\s*50\d{2}') { return @{ Kind = 'cu128'; Name = $nvidia } }
    return @{ Kind = 'cu118'; Name = $nvidia }
}

Write-Host 'VoiSee AI Runtime Installer - RVC backend'
Write-Host "Runtime root: $RuntimeRoot"
Write-Host "Pinned RVC commit: $PinnedCommit"

$python = Resolve-Python312
Write-Host "Python: $python"

$parent = Split-Path -Parent $RuntimeRoot
$tempRoot = Join-Path $env:TEMP ("VoiSee-RVC-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $parent, $tempRoot | Out-Null
$backup = $null
$runtimeReplaced = $false

try {
    Write-Step 'Downloading the pinned official RVC source'
    $zip = Join-Path $tempRoot 'rvc.zip'
    Invoke-WebRequest -Uri $RepoArchive -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $tempRoot -Force
    $expanded = Get-ChildItem $tempRoot -Directory | Where-Object { $_.Name -like 'Retrieval-based-Voice-Conversion-WebUI-*' } | Select-Object -First 1
    if (-not $expanded) { throw 'The downloaded RVC archive did not contain the expected directory.' }

    if (Test-Path $RuntimeRoot) {
        $backup = "$RuntimeRoot.previous"
        if (Test-Path $backup) { Remove-Item -Recurse -Force $backup }
        Move-Item $RuntimeRoot $backup
    }
    Move-Item $expanded.FullName $RuntimeRoot
    $runtimeReplaced = $true

    Write-Step 'Creating isolated Python environment'
    & $python -m venv (Join-Path $RuntimeRoot '.venv')
    $venvPython = Join-Path $RuntimeRoot '.venv\Scripts\python.exe'
    if (-not (Test-Path $venvPython)) { throw 'Python virtual environment was not created.' }
    & $venvPython -m pip install --upgrade pip setuptools wheel

    $profile = Get-NvidiaProfile
    Write-Host "Detected GPU: $($profile.Name)"
    Push-Location $RuntimeRoot
    try {
        if ($profile.Kind -eq 'cu128') {
            Write-Step 'Installing NVIDIA RTX 50-series CUDA runtime dependencies'
            & $venvPython -m pip install 'torch==2.7.1+cu128' 'torchaudio==2.7.1+cu128' --index-url 'https://download.pytorch.org/whl/cu128' --extra-index-url 'https://pypi.org/simple'
            if ($LASTEXITCODE -ne 0) { throw 'Torch CUDA 12.8 installation failed.' }
            $req = New-VoiSeeRequirements (Join-Path $RuntimeRoot 'requirments_cu128_py312.txt')
            try { & $venvPython -m pip install -r $req --index-url 'https://pypi.org/simple' }
            finally { Remove-Item -Force $req -ErrorAction SilentlyContinue }
        }
        elseif ($profile.Kind -eq 'cu118') {
            Write-Step 'Installing NVIDIA CUDA 11.8 runtime dependencies'
            & $venvPython -m pip install 'torch==2.7.1+cu118' 'torchaudio==2.7.1+cu118' --index-url 'https://download.pytorch.org/whl/cu118' --extra-index-url 'https://pypi.org/simple'
            if ($LASTEXITCODE -ne 0) { throw 'Torch CUDA 11.8 installation failed.' }
            $req = New-VoiSeeRequirements (Join-Path $RuntimeRoot 'requirments_cu118_py312.txt')
            try { & $venvPython -m pip install -r $req --index-url 'https://pypi.org/simple' }
            finally { Remove-Item -Force $req -ErrorAction SilentlyContinue }
        }
        else {
            Write-Step 'Installing CPU / AMD / Intel runtime dependencies'
            $req = New-VoiSeeRequirements (Join-Path $RuntimeRoot 'requirments_cpu_py312.txt')
            try {
                & $venvPython -m pip install -r $req --index-url 'https://pypi.org/simple' --extra-index-url 'https://download.pytorch.org/whl/cpu'
            }
            finally { Remove-Item -Force $req -ErrorAction SilentlyContinue }
        }
        if ($LASTEXITCODE -ne 0) { throw 'RVC dependency installation failed.' }

        Write-Step 'Downloading RVC inference assets'
        $downloadScript = @'
from huggingface_hub import snapshot_download, hf_hub_download
snapshot_download(repo_id="lj1995/VoiceConversionWebUI", revision="main", allow_patterns=["hubert_base/*"], local_dir="assets")
hf_hub_download(repo_id="lj1995/VoiceConversionWebUI", filename="rmvpe.pt", revision="main", local_dir="assets/rmvpe")
hf_hub_download(repo_id="lj1995/VoiceConversionWebUI", filename="rmvpe.onnx", revision="main", local_dir="assets/rmvpe")
'@
        & $venvPython -c $downloadScript
        if ($LASTEXITCODE -ne 0) { throw 'RVC inference asset download failed.' }
    }
    finally {
        Pop-Location
    }

    Write-Step 'Verifying runtime'
    $verify = @'
import os, sys, torch
from configs.config import Config
from infer import rtrvc
assert os.path.isfile(os.path.join("assets", "hubert_base", "pytorch_model.bin"))
assert os.path.isfile(os.path.join("assets", "hubert_base", "config.json"))
assert os.path.isfile(os.path.join("assets", "rmvpe", "rmvpe.pt"))
assert os.path.isfile(os.path.join("assets", "rmvpe", "rmvpe.onnx"))
cfg = Config()
print("torch=" + torch.__version__)
print("device=" + str(cfg.device))
print("cuda=" + str(torch.cuda.is_available()))
'@
    Push-Location $RuntimeRoot
    try {
        & $venvPython -c $verify
        if ($LASTEXITCODE -ne 0) { throw 'Runtime verification failed.' }
    }
    finally { Pop-Location }

    Set-Content -Path (Join-Path $RuntimeRoot '.voisee-runtime-version') -Value "rvc:$PinnedCommit" -Encoding ASCII
    if ($backup -and (Test-Path $backup)) {
        Remove-Item -Recurse -Force $backup
        $backup = $null
    }
    Write-Host "`nAI runtime installation completed successfully." -ForegroundColor Green
    Write-Host 'Return to VoiSee and activate the RVC voice again.'
}
catch {
    $failure = $_.Exception.Message
    try {
        if ($runtimeReplaced -and (Test-Path $RuntimeRoot)) {
            Remove-Item -Recurse -Force $RuntimeRoot
        }
        if ($backup -and (Test-Path $backup)) {
            Move-Item $backup $RuntimeRoot
            Write-Host 'The previous working AI runtime was restored.' -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "Runtime rollback also failed: $($_.Exception.Message)" -ForegroundColor Red
    }
    Write-Host "`nAI runtime installation failed: $failure" -ForegroundColor Red
    exit 1
}
finally {
    try { if (Test-Path $tempRoot) { Remove-Item -Recurse -Force $tempRoot } } catch {}
}
