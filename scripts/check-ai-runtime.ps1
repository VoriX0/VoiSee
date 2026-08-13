param(
    [string]$RuntimeRoot = "$env:LOCALAPPDATA\VoiSe\AI Runtime\rvc"
)

$ErrorActionPreference = 'Stop'
$python = Join-Path $RuntimeRoot '.venv\Scripts\python.exe'
$marker = Join-Path $RuntimeRoot '.voisee-runtime-version'
$hubertConfig = Join-Path $RuntimeRoot 'assets\hubert_base\config.json'
$hubertWeights = Join-Path $RuntimeRoot 'assets\hubert_base\pytorch_model.bin'
$rmvpe = Join-Path $RuntimeRoot 'assets\rmvpe\rmvpe.pt'
$rmvpeOnnx = Join-Path $RuntimeRoot 'assets\rmvpe\rmvpe.onnx'

Write-Host '=== VoiSee AI Runtime Check ==='
Write-Host "RuntimeRoot=$RuntimeRoot"
foreach ($item in @($python, $marker, $hubertConfig, $hubertWeights, $rmvpe, $rmvpeOnnx)) {
    if (Test-Path $item) { Write-Host "OK   $item" }
    else { Write-Host "MISS $item" }
}

if (-not (Test-Path $python)) {
    throw 'Runtime Python is missing. Use Settings -> AI Voice Runtime -> Install / Repair.'
}

Push-Location $RuntimeRoot
try {
    & $python -c 'import torch; from configs.config import Config; from infer import rtrvc; c=Config(); print("torch="+torch.__version__); print("device="+str(c.device)); print("half="+str(c.is_half))'
    if ($LASTEXITCODE -ne 0) { throw "Runtime import check failed (exit code $LASTEXITCODE)." }
}
finally {
    Pop-Location
}

Write-Host 'AI_RUNTIME_CHECK=PASS' -ForegroundColor Green
