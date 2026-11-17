#requires -Version 5.1
<#
  dsh-slots —— Unity 多实例"测试槽位"管理器

  主工作树（唯一真相源）:  sync 单向 →  slot（工程副本）  →  bridge 端口  →  测试  →  自动删除

  常用:
    powershell -File Tools\dsh-slots\slots.ps1 status
    powershell -File Tools\dsh-slots\slots.ps1 create -Name s1 [-WithLibrary]
    powershell -File Tools\dsh-slots\slots.ps1 sync   -Name s1
    powershell -File Tools\dsh-slots\slots.ps1 start  -Name s1 -Batch          # -batchmode -nographics
    powershell -File Tools\dsh-slots\slots.ps1 start  -Name s1 -Batch -RunTests -TestPlatform PlayMode
    powershell -File Tools\dsh-slots\slots.ps1 stop   -Name s1 [-Keep]         # 默认停掉即删除
    powershell -File Tools\dsh-slots\slots.ps1 remove -Name s1
    powershell -File Tools\dsh-slots\slots.ps1 sweep  [-TtlHours 6]            # 清理孤儿槽位
    powershell -File Tools\dsh-slots\slots.ps1 instances                        # 所有运行中 bridge 的 JSON

  约定:
    * slot 目录绝不放在任何 Assets/ 下。
    * 每个 slot 的 bridge 端口由 DshBridge 自己分配，从 <slot>/Temp/DshBridge/instance.json 读，
      不要写死端口；客户端应带 X-Dsh-Project 头 / project 字段做身份校验。
    * stop 默认删除副本（用户要求"测试完自动删除"）；-Keep 只停不删。
#>
[CmdletBinding()]
param(
  [Parameter(Position = 0)]
  [ValidateSet('status', 'instances', 'create', 'sync', 'start', 'stop', 'remove', 'sweep')]
  [string]$Command = 'status',

  [string]$Name = '',
  [string]$Config = '',

  [switch]$Batch,
  [switch]$Nographics,
  [switch]$RunTests,
  [ValidateSet('PlayMode', 'EditMode')][string]$TestPlatform = 'PlayMode',

  [switch]$Keep,
  [switch]$Force,
  [switch]$WithLibrary,
  [int]$WaitSeconds = 1200,
  [int]$TtlHours = 0,

  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$UnityArgs
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Config) { $Config = Join-Path $ScriptDir 'config.json' }

function Write-Step([string]$m) { Write-Host "[slots] $m" -ForegroundColor Cyan }
function Write-Warn2([string]$m) { Write-Host "[slots] $m" -ForegroundColor Yellow }

function Get-Cfg {
  if (-not (Test-Path -LiteralPath $Config)) { throw "config not found: $Config" }
  $c = Get-Content -Raw -LiteralPath $Config | ConvertFrom-Json
  if (-not $c.root) { throw "config.root missing" }
  if (-not $c.unity -or -not (Test-Path -LiteralPath $c.unity)) { throw "config.unity not found: $($c.unity)" }
  if (-not $c.source -or -not (Test-Path -LiteralPath $c.source)) { throw "config.source not found: $($c.source)" }
  $c
}

function Get-SlotPath($c, [string]$n) {
  if (-not $n) { throw "-Name is required" }
  if ($n -notmatch '^[a-z0-9][a-z0-9_-]{0,30}$') { throw "slot name must match ^[a-z0-9][a-z0-9_-]{0,30}$ (got '$n')" }
  Join-Path $c.root $n
}

function Get-Instance([string]$proj) {
  $f = Join-Path $proj 'Temp\DshBridge\instance.json'
  if (-not (Test-Path -LiteralPath $f)) { return $null }
  try { $d = Get-Content -Raw -LiteralPath $f | ConvertFrom-Json } catch { return $null }
  $alive = $false
  if ($d.pid) { $alive = [bool](Get-Process -Id $d.pid -ErrorAction SilentlyContinue) }
  [pscustomobject]@{
    project   = $d.project
    projectId = $d.projectId
    port      = [int]$d.port
    pid       = [int]$d.pid
    version   = $d.version
    batchMode = $d.batchMode
    updatedAt = $d.updatedAt
    alive     = $alive
    error     = $d.error
  }
}

function Invoke-Robocopy([string]$src, [string]$dst, [string[]]$extra) {
  New-Item -ItemType Directory -Force -Path $dst | Out-Null
  $base = @($src, $dst, '/MIR', '/R:2', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/MT:8',
            '/XD', 'Temp', 'Logs', 'obj', '.git', '.vs',
            '/XF', '*.dshclaim', '*.dshclaim.meta', '*.dshpending')
  $all = $base + $extra
  & robocopy @all | Out-Null
  $code = $LASTEXITCODE
  if ($code -ge 8) { throw "robocopy failed ($code): $src -> $dst" }
  $code
}

function Set-SlotCompany($slot, [string]$n) {
  $f = Join-Path $slot 'ProjectSettings\ProjectSettings.asset'
  if (-not (Test-Path -LiteralPath $f)) { Write-Warn2 "ProjectSettings.asset missing: $f"; return }
  $text = Get-Content -Raw -LiteralPath $f
  $suffix = "-dshslot-$n"
  $re = [regex]'(?m)^(?<k>\s*companyName:\s*)(?<v>.+)$'
  $m = $re.Match($text)
  if ($m.Success -and $m.Groups['v'].Value -notmatch 'dshslot') {
    $text = $re.Replace($text, [System.Text.RegularExpressions.MatchEvaluator]{
      param($mm) $mm.Groups['k'].Value + $mm.Groups['v'].Value.Trim() + $suffix
    }, 1)
    Set-Content -LiteralPath $f -Value $text -NoNewline -Encoding UTF8
    Write-Step ("persistentDataPath 隔离: companyName -> " + $m.Groups['v'].Value.Trim() + $suffix)
  }
}

function Wait-Bridge([string]$slot, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    $i = Get-Instance $slot
    if ($i -and $i.alive -and $i.port -gt 0) { return $i }
    Start-Sleep -Seconds 3
  }
  throw "bridge 未在 $timeoutSec 秒内就绪（看 $slot\Logs\editor.log）"
}

function Stop-UnityFor($slot) {
  $i = Get-Instance $slot
  if ($i -and $i.alive) {
    Write-Step ("停止 pid " + $i.pid)
    Stop-Process -Id $i.pid -Force -ErrorAction SilentlyContinue
  }
  try {
    Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue |
      Where-Object { $_.CommandLine -and $_.CommandLine.Contains($slot) } |
      ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  } catch { }
  for ($k = 0; $k -lt 20; $k++) {
    $still = $false
    if ($i -and $i.pid) { $still = [bool](Get-Process -Id $i.pid -ErrorAction SilentlyContinue) }
    if (-not $still) { break }
    Start-Sleep -Milliseconds 500
  }
  $lock = Join-Path $slot 'Temp\UnityLockfile'
  if (Test-Path -LiteralPath $lock) {
    $owner = $null
    try { $owner = (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($slot) } | Select-Object -First 1).ProcessId } catch { }
    if (-not $owner) { Remove-Item -LiteralPath $lock -Force -ErrorAction SilentlyContinue }
  }
}

function Archive-Log($c, [string]$slot, [string]$n) {
  if (-not $c.archiveLogs) { return }
  $src = Join-Path $slot 'Logs\editor.log'
  if (-not (Test-Path -LiteralPath $src)) { return }
  $dir = Join-Path $c.root 'logs'
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  $dst = Join-Path $dir ("{0}-{1:yyyyMMdd-HHmmss}.log" -f $n, (Get-Date))
  Copy-Item -LiteralPath $src -Destination $dst -Force -ErrorAction SilentlyContinue
}

function Remove-SlotDir([string]$slot) {
  for ($k = 0; $k -lt 5; $k++) {
    try {
      Remove-Item -LiteralPath $slot -Recurse -Force -ErrorAction Stop
      return
    } catch {
      if ($k -eq 4) { throw }
      Start-Sleep -Seconds 2
    }
  }
}

function Get-SlotSizeMB([string]$slot) {
  try {
    $b = (Get-ChildItem -LiteralPath $slot -Recurse -Force -File -ErrorAction SilentlyContinue |
          Measure-Object -Property Length -Sum).Sum
    if ($null -eq $b) { return 0 }
    [math]::Round($b / 1MB)
  } catch { 0 }
}

# ---------------------------------------------------------------- commands

function Cmd-Status($c) {
  if (-not (Test-Path -LiteralPath $c.root)) { Write-Host "no slots yet: $($c.root)"; return }
  $rows = @()
  Get-ChildItem -LiteralPath $c.root -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'logs' } | ForEach-Object {
      $i = Get-Instance $_.FullName
      $rows += [pscustomobject]@{
        slot    = $_.Name
        port    = if ($i) { $i.port } else { '-' }
        pid     = if ($i) { $i.pid } else { '-' }
        alive   = if ($i) { $i.alive } else { $false }
        sizeMB  = Get-SlotSizeMB $_.FullName
        created = $_.CreationTime.ToString('MM-dd HH:mm')
        path    = $_.FullName
      }
    }
  if (-not $rows) { Write-Host "no slots under $($c.root)"; return }
  $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}

function Cmd-Instances() {
  $dir = Join-Path $env:LOCALAPPDATA 'DshBridge\instances'
  $list = @()
  if (Test-Path -LiteralPath $dir) {
    Get-ChildItem -LiteralPath $dir -Filter '*.json' -ErrorAction SilentlyContinue | ForEach-Object {
      try {
        $d = Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json
        $alive = [bool](Get-Process -Id $d.pid -ErrorAction SilentlyContinue)
        if ($alive) {
          $list += [pscustomobject]@{
            project = $d.project; projectId = $d.projectId; port = [int]$d.port
            pid = [int]$d.pid; version = $d.version; batchMode = $d.batchMode
            unity = $d.unity; updatedAt = $d.updatedAt
          }
        } elseif ($Force) {
          Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
        }
      } catch { }
    }
  }
  $list | ConvertTo-Json -Depth 4
}

function Cmd-Create($c, [string]$n) {
  $slot = Get-SlotPath $c $n
  if (Test-Path -LiteralPath $slot) {
    if (-not $Force) { throw "slot already exists: $slot (use -Force to recreate)" }
    Write-Warn2 "recreating $slot"
    Stop-UnityFor $slot
    Remove-SlotDir $slot
  }
  $src = $c.source
  New-Item -ItemType Directory -Force -Path (Join-Path $slot 'Logs'), (Join-Path $slot 'TestResults') | Out-Null

  Write-Step "复制 Assets"
  Invoke-Robocopy (Join-Path $src 'Assets') (Join-Path $slot 'Assets') @() | Out-Null
  Write-Step "复制 Packages"
  Invoke-Robocopy (Join-Path $src 'Packages') (Join-Path $slot 'Packages') @() | Out-Null
  Write-Step "复制 ProjectSettings"
  Invoke-Robocopy (Join-Path $src 'ProjectSettings') (Join-Path $slot 'ProjectSettings') @() | Out-Null
  if (Test-Path -LiteralPath (Join-Path $src 'UserSettings')) {
    Copy-Item -LiteralPath (Join-Path $src 'UserSettings') -Destination $slot -Recurse -Force -ErrorAction SilentlyContinue
  }
  if ($WithLibrary -and (Test-Path -LiteralPath (Join-Path $src 'Library'))) {
    Write-Step "复制 Library（暖启动，跨路径仍可能部分重导入）"
    Invoke-Robocopy (Join-Path $src 'Library') (Join-Path $slot 'Library') @('/XD', 'Bee', 'PackageCache') | Out-Null
  }
  Set-SlotCompany $slot $n
  Write-Step "created: $slot ($(Get-SlotSizeMB $slot) MB)"
}

function Cmd-Sync($c, [string]$n) {
  $slot = Get-SlotPath $c $n
  if (-not (Test-Path -LiteralPath $slot)) { throw "slot not found: $slot" }
  $src = $c.source
  Write-Step "同步 Assets (镜像)"
  Invoke-Robocopy (Join-Path $src 'Assets') (Join-Path $slot 'Assets') @() | Out-Null
  Write-Step "同步 Packages / ProjectSettings"
  Invoke-Robocopy (Join-Path $src 'Packages') (Join-Path $slot 'Packages') @() | Out-Null
  Invoke-Robocopy (Join-Path $src 'ProjectSettings') (Join-Path $slot 'ProjectSettings') @() | Out-Null
  Set-SlotCompany $slot $n
  Write-Step "synced: $slot"
}

function Cmd-Start($c, [string]$n) {
  $slot = Get-SlotPath $c $n
  if (-not (Test-Path -LiteralPath $slot)) { throw "slot not found: $slot" }
  $i = Get-Instance $slot
  if ($i -and $i.alive) { Write-Step "already running: port=$($i.port) pid=$($i.pid)"; return $i }

  # 上次被 kill 的残留锁：确认没有进程持有再删
  $lock = Join-Path $slot 'Temp\UnityLockfile'
  if (Test-Path -LiteralPath $lock) { Remove-Item -LiteralPath $lock -Force -ErrorAction SilentlyContinue }

  $log = Join-Path $slot 'Logs\editor.log'
  $argStr = '-projectPath "{0}" -logFile "{1}"' -f $slot, $log
  if ($Batch -or $Nographics) { $argStr += ' -batchmode' }
  if ($Nographics) { $argStr += ' -nographics' }
  if ($RunTests) {
    $res = Join-Path $slot 'TestResults\result.xml'
    $argStr += ' -runTests -testPlatform ' + $TestPlatform + ' -testResults "' + $res + '"'
  }
  if ($UnityArgs) { $argStr += ' ' + ($UnityArgs -join ' ') }

  Write-Step "启动: $($c.unity) $argStr"
  $p = Start-Process -FilePath $c.unity -ArgumentList $argStr -PassThru
  Write-Step "启动 pid $($p.Id)，等待 bridge 就绪（最多 $WaitSeconds 秒）"
  $info = Wait-Bridge $slot $WaitSeconds
  Write-Step ("就绪: port=" + $info.port + " pid=" + $info.pid + " projectId=" + $info.projectId)
  $info
}

function Cmd-Stop($c, [string]$n) {
  $slot = Get-SlotPath $c $n
  if (-not (Test-Path -LiteralPath $slot)) { throw "slot not found: $slot" }
  Archive-Log $c $slot $n
  Stop-UnityFor $slot
  if ($Keep) { Write-Step "stopped (kept): $slot"; return }
  Remove-SlotDir $slot
  Write-Step "stopped & deleted: $slot"
}

function Cmd-Remove($c, [string]$n) {
  $slot = Get-SlotPath $c $n
  if (-not (Test-Path -LiteralPath $slot)) { throw "slot not found: $slot" }
  $i = Get-Instance $slot
  if ($i -and $i.alive -and -not $Force) { throw "slot is running (pid $($i.pid)); use stop, or remove -Force" }
  Archive-Log $c $slot $n
  Stop-UnityFor $slot
  Remove-SlotDir $slot
  Write-Step "removed: $slot"
}

function Cmd-Sweep($c) {
  if (-not (Test-Path -LiteralPath $c.root)) { Write-Host "no slots"; return }
  $ttl = if ($TtlHours -gt 0) { $TtlHours } else { [int]$c.ttlHours }
  $now = Get-Date
  $removed = 0
  Get-ChildItem -LiteralPath $c.root -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'logs' } | ForEach-Object {
      $i = Get-Instance $_.FullName
      $ageH = ($now - $_.CreationTime).TotalHours
      if ($i -and $i.alive) { return }
      if (-not $Force -and $ageH -lt $ttl) { return }
      Archive-Log $c $_.FullName $_.Name
      Stop-UnityFor $_.FullName
      try { Remove-SlotDir $_.FullName; $removed++; Write-Step ("sweep 删除 " + $_.Name) } catch { Write-Warn2 ("sweep 失败 " + $_.Name + ": " + $_.Exception.Message) }
    }
  Write-Step "sweep done, removed $removed"
}

# ---------------------------------------------------------------- main

$cfg = Get-Cfg
switch ($Command) {
  'status' { Cmd-Status $cfg }
  'instances' { Cmd-Instances }
  'create' { Cmd-Create $cfg $Name }
  'sync' { Cmd-Sync $cfg $Name }
  'start' { Cmd-Start $cfg $Name | ConvertTo-Json -Depth 4 }
  'stop' { Cmd-Stop $cfg $Name }
  'remove' { Cmd-Remove $cfg $Name }
  'sweep' { Cmd-Sweep $cfg }
}
