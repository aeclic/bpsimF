param(
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label = 'current',
    [ValidateSet('Parity', 'Performance', 'Sampling', 'Metadata')][string]$Suite = 'Parity',
    [int]$TimeoutSeconds = 240,
    [string]$NativeDll
)
$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $workspace '_codex_work/anvil-parity/project'
$running = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.Contains($project) }
if ($running) { throw 'The private parity editor is already running; inspect it before starting another run' }
$nativeArgument = if ($NativeDll) { @('--native-dll', (Resolve-Path -LiteralPath $NativeDll).Path) } else { @() }
& python -X utf8 (Join-Path $PSScriptRoot 'prepare_project.py') @nativeArgument
if ($LASTEXITCODE -ne 0) { throw 'Private physics compilation failed' }
$unity = (Get-Content -LiteralPath (Join-Path $workspace 'Tools/dsh-slots/config.json') -Raw | ConvertFrom-Json).unity
$log = Join-Path $project ('Logs/' + $Label + '-editor.log')
[System.IO.Directory]::CreateDirectory((Split-Path -Parent $log)) | Out-Null
$entry = switch ($Suite) { 'Performance' { 'Bpsim.QA.AnvilParityQA.RunPerformanceBatch' }; 'Sampling' { 'Bpsim.QA.AnvilParityQA.RunSamplingProbeBatch' }; 'Metadata' { 'Bpsim.QA.AnvilParityQA.RunMetadataContractsBatch' }; default { 'Bpsim.QA.AnvilParityQA.RunBatch' } }
$reportKind = switch ($Suite) { 'Performance' { 'performance' }; 'Sampling' { 'sampling' }; 'Metadata' { 'metadata' }; default { 'parity' } }
$arguments = @('-batchmode','-nographics','-quit','-disable-assembly-updater',
    '-projectPath',('"' + $project + '"'),'-executeMethod',$entry,
    '-anvil-parity-label',$Label,'-logFile',('"' + $log + '"'))
$started = Get-Date
$process = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('Private Unity PID=' + $process.Id)
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    throw ('Private Unity has not exited; no retry or termination was issued. Inspect ' + $log)
}
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath $log -Tail 35
    throw ('Private Unity failed with exit ' + $process.ExitCode)
}
$report = Join-Path $project ('Logs/anvil-' + $reportKind + '-' + $Label + '.json')
if (-not (Test-Path -LiteralPath $report) -or (Get-Item -LiteralPath $report).LastWriteTime -lt $started) {
    throw ('No fresh parity report was produced. Inspect ' + $log)
}
$destination = Join-Path $workspace ('Logs/anvil-' + $reportKind + '-' + $Label + '.json')
Copy-Item -LiteralPath $report -Destination $destination
$data = Get-Content -LiteralPath $destination -Raw | ConvertFrom-Json
if ($Suite -eq 'Sampling') {
    $data | Select-Object pass,contacts,defaultUniqueHashes,mixedUniqueHashes,defaultLargestGroup,mixedLargestGroup,defaultP95Ms,mixedP95Ms | Format-List
} elseif ($Suite -eq 'Metadata') {
    Write-Output ('Metadata checks=' + $data.measurements.Count + ' failures=' + $data.failures)
    $data.measurements | Select-Object backend,scenario,value,tolerance,pass | Format-Table -AutoSize
} elseif ($Suite -eq 'Performance') {
    Write-Output ('Performance cases=' + $data.cases.Count + ' errors=' + $data.errors.Count)
    $data.cases | Select-Object backend,scenario,@{Name='p95Ms';Expression={$_.total.p95}},@{Name='nativeP95Ms';Expression={$_.native.p95}},meanAllocatedBytes | Format-Table -AutoSize
    $data.errors | Write-Output
} else {
    Write-Output ('Physics measurements=' + $data.measurements.Count + ' requiredFailures=' + $data.failures + ' referenceDeviations=' + $data.referenceDeviations)
    $data.measurements | Where-Object { -not $_.pass } | Select-Object backend,scenario,metric,value,tolerance,referenceOnly,detail | Format-Table -AutoSize
}
Write-Output ('Report: ' + $destination)
