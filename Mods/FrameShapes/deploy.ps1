param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [string]$UnityEditor = 'D:\folder\2022.2.23f1\Editor',
    [string]$ReferenceAssembly
)
$ErrorActionPreference = 'Stop'
$gameRoot = [IO.Path]::GetFullPath($GameDir)
$managed = Join-Path $gameRoot 'bpsim_Data\Managed'
if (!(Test-Path -LiteralPath $managed)) { throw "Not a bpsim Mono player: $gameRoot" }
$runtime = Join-Path $PSScriptRoot 'Runtime'
if (!(Test-Path -LiteralPath (Join-Path $runtime 'BepInEx\core\BepInEx.dll'))) {
    $archive = Join-Path $PSScriptRoot 'BepInEx_win_x64_5.4.23.2.zip'
    Invoke-WebRequest 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip' -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $runtime -Force
    Remove-Item -LiteralPath $archive
}
Copy-Item -LiteralPath (Join-Path $runtime 'winhttp.dll') -Destination $gameRoot -Force
if (!(Test-Path -LiteralPath (Join-Path $gameRoot 'doorstop_config.ini'))) {
    Copy-Item -LiteralPath (Join-Path $runtime 'doorstop_config.ini') -Destination $gameRoot
}
$core = Join-Path $gameRoot 'BepInEx\core'
$pluginDir = Join-Path $gameRoot 'BepInEx\plugins\Bpsim.FrameShapes'
New-Item -ItemType Directory -Force -Path $core,$pluginDir | Out-Null
Copy-Item -Path (Join-Path $runtime 'BepInEx\core\*') -Destination $core -Force
$configDir = Join-Path $gameRoot 'BepInEx\config'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
$configPath = Join-Path $configDir 'BepInEx.cfg'
if (Test-Path -LiteralPath $configPath) {
    $configText = [IO.File]::ReadAllText($configPath)
    # Application initializes before bpsim's startup scene cleanup and loses the manager.
    # Keep explicitly customized entry points; migrate only the BepInEx default.
    $configText = $configText -replace '(?m)^Type[ \t]*=[ \t]*Application[ \t]*\r?$', 'Type = Camera'
} else {
    $configText = "[Preloader.Entrypoint]`r`nAssembly = UnityEngine.CoreModule.dll`r`nType = Camera`r`nMethod = .cctor`r`n"
}
[IO.File]::WriteAllText($configPath, $configText, [Text.UTF8Encoding]::new($false))
if (!$ReferenceAssembly) { $ReferenceAssembly = Join-Path $managed 'Assembly-CSharp.dll' }
$refs = @($ReferenceAssembly, (Join-Path $core 'BepInEx.dll'))
foreach ($name in @('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','Unity.Entities.dll')) {
    $refs += Join-Path $managed $name
}
$pluginDll = Join-Path $pluginDir 'Bpsim.FrameShapes.dll'
$compilerArgs = @((Join-Path $UnityEditor 'Data\DotNetSdkRoslyn\csc.dll'), '-nologo', '-target:library', '-nostdlib+', '-langversion:9', "-out:$pluginDll")
$compilerArgs += $refs | ForEach-Object { "-r:$_" }
$compilerArgs += Join-Path $PSScriptRoot 'FrameShapesPlugin.cs'
& (Join-Path $UnityEditor 'Data\NetCoreRuntime\dotnet.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'FrameShapes plugin compilation failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $pluginDir -Force
Write-Output "Installed: $pluginDll"
