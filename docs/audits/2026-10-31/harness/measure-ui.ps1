$ErrorActionPreference = 'Stop'
$auditRoot = $PSScriptRoot
$auditExe = Join-Path $auditRoot 'ui-baseline\src\VoiSe.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\VoiSe.App.exe'
$env:VOISEE_AUDIT = '1'
$auditProcess = Start-Process -FilePath $auditExe -ArgumentList '--background' -WorkingDirectory (Split-Path -Parent $auditExe) -WindowStyle Hidden -PassThru
$auditClock = [Diagnostics.Stopwatch]::StartNew()
$auditSamples = [Collections.Generic.List[object]]::new()
while ($auditClock.Elapsed.TotalSeconds -lt 45) {
    Start-Sleep -Milliseconds 500
    $auditProcess.Refresh()
    if ($auditProcess.HasExited) { break }
    $auditSamples.Add([pscustomobject]@{
        seconds = $auditClock.Elapsed.TotalSeconds
        cpuSeconds = $auditProcess.TotalProcessorTime.TotalSeconds
        privateMiB = $auditProcess.PrivateMemorySize64 / 1MB
        workingSetMiB = $auditProcess.WorkingSet64 / 1MB
        threads = $auditProcess.Threads.Count
        handles = $auditProcess.HandleCount
    })
}
$auditStable = @($auditSamples | Where-Object { $_.seconds -ge 10 -and $_.seconds -le 30 })
$auditSummary = [ordered]@{
    baseline = 'b1829bb; diagnostic copy with isolated empty data, no audio autostart, no Discord mute, --background'
    processId = $auditProcess.Id
    sampleCount = $auditSamples.Count
    exited = $auditProcess.HasExited
    exitCode = $(if ($auditProcess.HasExited) { $auditProcess.ExitCode } else { $null })
    steadySamples = $auditStable.Count
}
if ($auditStable.Count -gt 1) {
    $auditSummary.oneCoreCpuPercent = 100 * ($auditStable[-1].cpuSeconds - $auditStable[0].cpuSeconds) / ($auditStable[-1].seconds - $auditStable[0].seconds)
    $auditSummary.averagePrivateMiB = ($auditStable | Measure-Object privateMiB -Average).Average
    $auditSummary.averageWorkingSetMiB = ($auditStable | Measure-Object workingSetMiB -Average).Average
    $auditSummary.maxPrivateMiB = ($auditSamples | Measure-Object privateMiB -Maximum).Maximum
    $auditSummary.maxWorkingSetMiB = ($auditSamples | Measure-Object workingSetMiB -Maximum).Maximum
}
$auditSummary.samples = @($auditSamples)
$auditJson = $auditSummary | ConvertTo-Json -Depth 5
$auditJson | Set-Content -LiteralPath (Join-Path $auditRoot 'ui-idle.json') -Encoding utf8
$auditSummary.Remove('samples')
$auditSummary | ConvertTo-Json
