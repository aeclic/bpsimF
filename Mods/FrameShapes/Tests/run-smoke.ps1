param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [string]$UnityEditor = 'D:\folder\2022.2.23f1\Editor',
    [switch]$NoGraphics
)
$ErrorActionPreference = 'Stop'
$gameRoot = [IO.Path]::GetFullPath($GameDir)
$managed = Join-Path $gameRoot 'bpsim_Data\Managed'
$probeDir = Join-Path $gameRoot 'BepInEx\plugins\FrameShapesQA'
$outputDir = Join-Path $gameRoot 'output'
New-Item -ItemType Directory -Force -Path $probeDir,$outputDir | Out-Null
$probeDll = Join-Path $probeDir 'Bpsim.FrameShapes.Smoke.dll'
$response = Join-Path $probeDir 'compile.rsp'
$lines = @('-nologo','-target:library','-nostdlib+','-langversion:9', "-out:`"$probeDll`"")
$lines += Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object { '-r:"' + $_.FullName + '"' }
$lines += '-r:"' + (Join-Path $gameRoot 'BepInEx\core\BepInEx.dll') + '"'
$lines += '"' + (Join-Path $PSScriptRoot 'SmokePlugin.cs') + '"'
[IO.File]::WriteAllLines($response, $lines, [Text.UTF8Encoding]::new($false))
& (Join-Path $UnityEditor 'Data\NetCoreRuntime\dotnet.exe') (Join-Path $UnityEditor 'Data\DotNetSdkRoslyn\csc.dll') "@$response"
if ($LASTEXITCODE -ne 0) { throw 'Acceptance probe compilation failed' }
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = Join-Path $gameRoot 'bpsim.exe'
$startInfo.WorkingDirectory = $gameRoot
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.Arguments = '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 --frame-shapes-smoke-test -logFile "' + (Join-Path $outputDir 'frame-shapes-player.log') + '"'
if ($NoGraphics) { $startInfo.Arguments += ' -batchmode -nographics' }
$startedUtc = [DateTime]::UtcNow
$player = [Diagnostics.Process]::Start($startInfo)
Write-Output "Smoke test process: $($player.Id)"
if (!$player.WaitForExit(120000)) {
    $player.Kill()
    throw 'Acceptance player did not exit within 120 seconds; inspect output/frame-shapes-player.log'
}
if ($player.ExitCode -ne 0) { throw "Acceptance player exited with $($player.ExitCode)" }
$reportPath = Join-Path $outputDir 'frame-shapes-smoke.txt'
if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $startedUtc) {
    throw 'Acceptance player did not produce a new report'
}
$report = [IO.File]::ReadAllText($reportPath)
if ($report -notmatch 'FAILURES: 0' -or $report -match '(?m)^FAIL |^FAILURES: [1-9]') {
    throw "Acceptance failed: $reportPath"
}
Write-Output "Acceptance passed: $reportPath"
