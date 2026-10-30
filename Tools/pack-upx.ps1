<#
.SYNOPSIS
    UPX-pack a built Unity Windows player (bpsim) without breaking it.

.DESCRIPTION
    Packs only native PE files. Managed assemblies cannot be packed by UPX
    ("CantPackException: .NET files are not yet supported"), and the Mono
    runtime DLL must stay unpacked: a packed mono-2.0-bdwgc.dll access-violates
    at +0x145b70 right after "Mono config path" (exit 0xC0000005 / 0xB00).

.PARAMETER PlayerDir
    Folder containing <player>.exe, UnityPlayer.dll and <player>_Data.

.PARAMETER UpxPath
    upx.exe path. Default: Tools\upx\upx.exe, then PATH; if missing it is
    downloaded to Tools\upx\.

.PARAMETER Verify
    Launch the packed player for -VerifySeconds and assert it stays alive.

.PARAMETER Restore
    Unpack every packed file (upx -d).

.EXAMPLE
    .\Tools\pack-upx.ps1 -PlayerDir 'D:\build\bpsim_win64' -Verify

.EXAMPLE
    .\Tools\pack-upx.ps1 -PlayerDir 'D:\build\bpsim_win64' -Restore
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PlayerDir,
    [string]$UpxPath,
    [switch]$Verify,
    [int]$VerifySeconds = 40,
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Mono runtime DLLs: MUST stay unpacked (0xC0000005 on boot).
$NeverPack = @('mono-2.0-bdwgc.dll', 'MonoPosixHelper.dll')

function Resolve-Upx {
    param([string]$Hint)
    if ($Hint) {
        if (-not (Test-Path $Hint)) { throw "upx.exe not found: $Hint" }
        return (Resolve-Path $Hint).Path
    }
    $local = Join-Path $PSScriptRoot 'upx'
    foreach ($c in @((Join-Path $local 'upx.exe'), (Join-Path $local 'upx-4.2.4-win64\upx.exe'))) {
        if (Test-Path $c) { return $c }
    }
    $cmd = Get-Command upx.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    New-Item -ItemType Directory -Force -Path $local | Out-Null
    $zip = Join-Path $local 'upx.zip'
    Write-Host 'downloading upx 4.2.4 ...'
    Invoke-WebRequest -Uri 'https://github.com/upx/upx/releases/download/v4.2.4/upx-4.2.4-win64.zip' -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $local -Force
    Remove-Item $zip -Force
    foreach ($c in @((Join-Path $local 'upx.exe'), (Join-Path $local 'upx-4.2.4-win64\upx.exe'))) {
        if (Test-Path $c) { return $c }
    }
    throw 'upx.exe download/extract failed'
}

function Test-IsDotNet([string]$Path) {
    # CLI metadata root signature "BSJB"
    # Stream the whole file: mixed-mode (C++/CLI) images such as Havok.Vdb.dll
    # carry the CLI metadata root megabytes in (offset ~4.4 MB of 5.5 MB), so a
    # 4 KB / 1 MB window misses them and UPX then refuses the file.
    $fs = [IO.File]::OpenRead($Path)
    try {
        $chunk = New-Object byte[] (1MB)
        $carry = ''
        while (($n = $fs.Read($chunk, 0, $chunk.Length)) -gt 0) {
            $s = $carry + [Text.Encoding]::ASCII.GetString($chunk, 0, $n)
            if ($s.Contains('BSJB')) { return $true }
            $carry = if ($s.Length -ge 4) { $s.Substring($s.Length - 4) } else { $s }
        }
        return $false
    } finally { $fs.Dispose() }
}

function Test-IsPacked([string]$Path) {
    $fs = [IO.File]::OpenRead($Path)
    try {
        $buf = New-Object byte[] 4096
        $n = $fs.Read($buf, 0, $buf.Length)
        return [Text.Encoding]::ASCII.GetString($buf, 0, $n).Contains('UPX!')
    } finally { $fs.Dispose() }
}

$root = (Resolve-Path $PlayerDir).Path
$upx = Resolve-Upx -Hint $UpxPath
Write-Host "upx       : $upx"
Write-Host "player dir: $root"

$exe = Get-ChildItem $root -Filter *.exe |
    Where-Object { $_.Name -notmatch 'CrashHandler' } |
    Sort-Object Length -Descending | Select-Object -First 1
if (-not $exe) { throw "no player exe in $root" }

$targets = Get-ChildItem $root -Recurse -File |
    Where-Object { $_.Extension -in '.exe', '.dll' } |
    Where-Object { $_.DirectoryName -notlike '*_Data\Managed*' } |
    Where-Object { $NeverPack -notcontains $_.Name } |
    Sort-Object Length -Descending

$before = 0L
$after = 0L
$rows = @()

if ($Restore) {
    foreach ($f in $targets) {
        if (Test-IsPacked $f.FullName) {
            & $upx '-d' '-q' '--' $f.FullName | Out-Null
            Write-Host ("unpacked  {0}" -f $f.Name)
        }
    }
    return
}

foreach ($f in $targets) {
    $b = $f.Length
    $before += $b
    $status = 'packed'
    if (Test-IsDotNet $f.FullName) {
        $status = 'skip-dotnet'
    } elseif (Test-IsPacked $f.FullName) {
        $status = 'already'
    } else {
        # native stderr must not become a terminating error under Stop
        $prev = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $out = & $upx '--lzma' '-9' '-q' '--' $f.FullName 2>&1
            $code = $LASTEXITCODE
        } finally { $ErrorActionPreference = $prev }
        if ($code -ne 0) {
            $status = 'upx-failed'
            Write-Warning ("{0}: {1}" -f $f.Name, ($out -join ' '))
        }
    }
    $a = (Get-Item $f.FullName).Length
    $after += $a
    $rows += [pscustomobject]@{
        File    = $f.FullName.Substring($root.Length + 1)
        Status  = $status
        Before  = $b
        After   = $a
        SavedPc = if ($b -gt 0) { [math]::Round(100 * ($b - $a) / $b, 1) } else { 0 }
    }
}

$rows | Sort-Object { $_.Before - $_.After } -Descending |
    Format-Table File, Status, Before, After, SavedPc -AutoSize | Out-String -Width 200 | Write-Host

$dirBytes = (Get-ChildItem $root -Recurse -File | Measure-Object Length -Sum).Sum
$savedPc = 100 * ($before - $after) / [math]::Max(1, $before)
Write-Host ("native PE : {0:N1} MB -> {1:N1} MB  (saved {2:N1} MB / {3:N1}%)" -f ($before / 1MB), ($after / 1MB), (($before - $after) / 1MB), $savedPc)
Write-Host ("whole dir : {0:N1} MB" -f ($dirBytes / 1MB))

if ($Verify) {
    $log = Join-Path $env:TEMP ('upx-verify-{0}.log' -f (Get-Date -Format 'HHmmss'))
    $launch = @{
        FilePath         = $exe.FullName
        ArgumentList     = @('-logFile', $log, '-screen-width', '800', '-screen-height', '600', '-screen-fullscreen', '0')
        WorkingDirectory = $root
        PassThru         = $true
    }
    $p = Start-Process @launch
    for ($i = 0; $i -lt $VerifySeconds; $i += 2) {
        Start-Sleep -Seconds 2
        if ($p.HasExited) { break }
    }
    if ($p.HasExited) {
        Write-Warning ('player exited early, code=0x{0:X}' -f $p.ExitCode)
        $ev = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'Application Error'; StartTime = (Get-Date).AddMinutes(-2) } -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($ev) {
            ($ev.Message -split "\r?\n") | Where-Object { $_ -match 'mono|dll|Exception|0x' } | Select-Object -First 3 | ForEach-Object { Write-Warning $_ }
        }
        Write-Host 'VERIFY: FAILED'
    } else {
        Stop-Process -Id $p.Id -Force
        if (Test-Path $log) {
            Write-Host 'stage reached:'
            Get-Content $log | Select-Object -Last 6 | ForEach-Object { Write-Host ("  " + $_) }
        }
        Write-Host 'VERIFY: OK (player stayed alive)'
    }
}
