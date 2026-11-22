param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label,
    [string[]]$Saves = @('bpsim1号机', '不败荣光'),
    [ValidateSet('AnvilPhysics', 'UnityPhysics', 'HavokPhysics')][string[]]$Backends = @('AnvilPhysics', 'UnityPhysics', 'HavokPhysics'),
    [ValidateRange(1, 10)][int]$Repeats = 2,
    [ValidateRange(4, 16)][int]$Solver = 8,
    [ValidateRange(1, 16)][int]$Workers = 4,
    [ValidateRange(1, 256)][int]$WarmupSteps = 32,
    [ValidateRange(8, 1024)][int]$SampleSteps = 128,
    [ValidateRange(30, 900)][int]$TimeoutSeconds = 90,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '../..'),
    [string]$PlayerExe,
    [ValidateRange(-1, 100)][int]$BridgeVersion = -1,
    [ValidateRange(0, 256)][int]$TraceSteps = 0,
    [switch]$TraceIgnoreHalfColliders,
    [switch]$Unbreakable
)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path -LiteralPath $ProjectPath).Path
if ($PlayerExe) {
    $PlayerExe = (Resolve-Path -LiteralPath $PlayerExe).Path
    New-Item -ItemType Directory -Force -Path (Join-Path $workspace 'Logs') | Out-Null
} else {
    $instance = Get-Content -LiteralPath (Join-Path $workspace 'Temp/DshBridge/instance.json') -Raw | ConvertFrom-Json
    $bridge = 'http://127.0.0.1:' + [int]$instance.port
}

function Assert-Ready {
    $editor = Invoke-RestMethod -Uri "$bridge/ping" -TimeoutSec 5
    if ($editor.project -ne $workspace -or $editor.pid -ne $instance.pid -or -not $editor.playing -or $editor.compiling -or $editor.mainThreadStalled) {
        throw 'The pinned project must be running in a stable Play Mode.'
    }
}

for ($repeat = 0; $repeat -lt $Repeats; $repeat++) {
    for ($saveIndex = 0; $saveIndex -lt $Saves.Count; $saveIndex++) {
        $save = $Saves[$saveIndex]
        if ($save -match '[;=\\/]' -or -not (Test-Path -LiteralPath (Join-Path $workspace "savefile/$save") -PathType Leaf)) {
            throw "Unknown test save: $save"
        }
        for ($backendIndex = 0; $backendIndex -lt $Backends.Count; $backendIndex++) {
            $index = if ($repeat % 2 -eq 0) { $backendIndex } else { $Backends.Count - 1 - $backendIndex }
            $backend = $Backends[$index]
            $runLabel = "$Label-$saveIndex-$backend-$repeat"
            $reportPath = Join-Path $workspace "Logs/spinperf-$runLabel.json"
            if (Test-Path -LiteralPath $reportPath) { throw "Report already exists: $reportPath" }
            $startupSeconds = [Math]::Min(180, $TimeoutSeconds - 10)
            $spec = "file=savefile/$save;backend=$backend;solver=$Solver;workers=$Workers;gravity=-9.81;spin=0;writev=0;warmupsteps=$WarmupSteps;steps=$SampleSteps;startupseconds=$startupSeconds;bisect=0;sweep=0;mode=rigid;label=$runLabel"
            if ($Unbreakable) { $spec += ';inf=1' }
            if ($TraceSteps -gt 0) { $spec += ";tracesteps=$TraceSteps" }
            if ($TraceIgnoreHalfColliders) { $spec += ';traceignorehalfcolliders=1' }
            if ($PlayerExe) {
                $log = Join-Path $workspace "Logs/$runLabel-player.log"
                $arguments = @('-batchmode', '-screen-width', '640', '-screen-height', '480', '-screen-fullscreen', '0',
                    '-bpsimSaveBenchmark', ('"' + $spec + '"'), '-logFile', ('"' + $log + '"'))
                $start = [System.Diagnostics.ProcessStartInfo]::new($PlayerExe, ($arguments -join ' '))
                $start.WorkingDirectory = $workspace
                $start.UseShellExecute = $false
                $start.CreateNoWindow = $true
                $start.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
                $player = [System.Diagnostics.Process]::Start($start)
                try {
                    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
                    while (-not $player.WaitForExit(1000) -and [datetime]::UtcNow -lt $deadline) { }
                    if (-not $player.HasExited) {
                        $player.Kill()
                        throw "Player timed out for $runLabel. See $log"
                    }
                    if ($player.ExitCode -ne 0) { throw "Player exit $($player.ExitCode) for $runLabel. See $log" }
                } finally {
                    $player.Dispose()
                }
            } else {
                Assert-Ready
                $request = @{method='invoke_static';params=@{type='Bpsim.QA.SpinPerfProbe';method='Run';args=@($spec)}} | ConvertTo-Json -Depth 5 -Compress
                $response = Invoke-RestMethod -Uri "$bridge/api" -Method Post -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($request)) -TimeoutSec 25
                if (-not $response.ok -or $response.result -notlike 'started:*') { throw ($response | ConvertTo-Json -Compress) }
                $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
                do {
                    Start-Sleep -Milliseconds 250
                } until ((Test-Path -LiteralPath $reportPath) -or [datetime]::UtcNow -ge $deadline)
            }
            if (-not (Test-Path -LiteralPath $reportPath)) { throw "No report for $runLabel; inspect Unity before starting another run." }
            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
            $sample = $report.phases | Where-Object { $_.label -like 'single*' }
            if ($report.error -or ($PlayerExe -and $report.host -ne 'Player') -or $report.actualBackend -ne $backend -or $sample.completedPhysicsSteps -ne $SampleSteps -or `
                ($backend -eq 'AnvilPhysics' -and ($sample.anvilStepDelta -ne $SampleSteps -or `
                    ($BridgeVersion -ge 0 -and $report.nativeBridgeVersion -ne $BridgeVersion)))) {
                throw "Invalid measurement $runLabel ($($report.error)); see $reportPath"
            }
            '{0}: {1} parts / {2} ECS joints; step p50 {3:F3} ms / p95 {4:F3} ms, anchors RMS {5:G4} m' -f `
                $runLabel, $report.parts, $report.jointEntities, $sample.physicsP50Ms, $sample.physicsP95Ms, $sample.rmsFixedAnchorError
        }
    }
}
