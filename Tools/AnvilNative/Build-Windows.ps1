[CmdletBinding(DefaultParameterSetName = 'Zig')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Clang')][string]$Clang,
    [Parameter(Mandatory, ParameterSetName = 'Zig')][string]$Zig,
    [string]$Output,
    [switch]$LegacyBridge,
    [switch]$SparseWeld,
    [switch]$ExperimentalIntegratedSubsteps
)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratch = Join-Path $workspace '_codex_work/anvil-native'
$archive = Join-Path $scratch 'box3d-e77352c.tar.gz'
$commit = 'e77352cd606dc1a34209094076199549a52ea0a1'
$sha256 = 'BF2719F6A5389723C285662046809F75852D865039FF39F440853B0DDBA0A9E7'
if (-not $Output) { $Output = Join-Path $scratch 'box3d.dll' }
$Output = [IO.Path]::GetFullPath($Output)
if ($Output.StartsWith((Join-Path $workspace 'Assets') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build outside Assets, then run the ABI and parity checks before installing the DLL.'
}

New-Item -ItemType Directory -Force -Path $scratch, (Split-Path -Parent $Output) | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    curl.exe --fail --location --silent --show-error "https://codeload.github.com/erincatto/box3d/tar.gz/e77352c" -o $archive
    if ($LASTEXITCODE -ne 0) { throw "Unable to download Box3D $commit" }
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $sha256) {
    throw "Unexpected Box3D archive hash for $commit; replace $archive with the pinned archive."
}

$build = Join-Path $scratch ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $build | Out-Null
try {
    tar.exe -xf $archive -C $build
    if ($LASTEXITCODE -ne 0) { throw 'Box3D extraction failed.' }
    $source = Join-Path $build 'box3d-e77352c'
    $relative = [IO.Path]::GetRelativePath($workspace, $source).Replace('\', '/')
    $patches = @('hinge-wrap.patch')
    if ($SparseWeld) { $patches += 'weld-sparse.patch' }
    $defines = @()
    if (-not $LegacyBridge -and $ExperimentalIntegratedSubsteps) {
        $patches += 'integrated-substeps.patch'
        $defines += '-DANVIL_INTEGRATED_SUBSTEPS'
    }
    foreach ($patch in $patches) {
        & git -C $workspace apply --recount --whitespace=nowarn --directory=$relative (Join-Path $PSScriptRoot $patch)
        if ($LASTEXITCODE -ne 0) { throw "Cannot apply $patch to pinned Box3D." }
    }
    $sources = @(Get-ChildItem -LiteralPath "$source/src" -Filter '*.c' | Sort-Object Name | ForEach-Object FullName)
    $sources += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'anvil_*.c' | Where-Object {
        -not $LegacyBridge -or $_.Name -ne 'anvil_step.c'
    } | Sort-Object Name | ForEach-Object FullName)
    $compiler = if ($Zig) { $Zig } else { $Clang }
    $driverArgs = if ($Zig) { @('cc', '-target', 'x86_64-windows-gnu') } else { @('-target', 'x86_64-w64-windows-gnu') }
    & $compiler @driverArgs '-O2' '-ffp-contract=off' '-DNDEBUG' '-DBOX3D_AVX2' '-Dbox3d_EXPORTS' @defines `
        '-I' "$source/include" '-I' "$source/src" '-shared' '-o' $Output @sources
    if ($LASTEXITCODE -ne 0) { throw 'Box3D compilation failed.' }
    Get-FileHash -LiteralPath $Output -Algorithm SHA256
} finally {
    $resolved = (Resolve-Path -LiteralPath $build).Path
    if ($resolved.StartsWith($scratch + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
