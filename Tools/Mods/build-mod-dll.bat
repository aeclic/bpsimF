@echo off
rem ---------------------------------------------------------------------------
rem Precompile a mod's Code/*.cs into a DLL for shipping in a player build.
rem
rem Why this step exists:
rem   Standalone Mono players ship without mcs.exe and without
rem   Microsoft.CSharp / System.CodeDom, so ModScriptCompiler's runtime source
rem   compilation always fails there. Player.log shows:
rem     "[Mods] <id> 加载失败：源码编译异常：Error running ...\mono\mini\mono.exe"
rem   So mods must be precompiled and declared via <Assemblies> in Mod.xml.
rem
rem Usage:
rem   Tools\Mods\build-mod-dll.bat <mod-folder> <Output.dll>
rem   e.g. Tools\Mods\build-mod-dll.bat bpreboot-specials BprebootSpecials.dll
rem
rem Prerequisite: run a player build once (default Builds\ModSpecials) so the
rem reference assemblies exist. Re-run both after touching mod source or the
rem host API surface.
rem ---------------------------------------------------------------------------
setlocal

if "%~1"=="" goto usage
if "%~2"=="" goto usage

set "PROJ=%~dp0..\.."
pushd "%PROJ%" || exit /b 1
set "PROJ=%CD%"
popd

set "MCS=D:\folder\2022.2.23f1\Editor\Data\MonoBleedingEdge\bin\mcs.bat"
set "MANAGED=%PROJ%\Builds\ModSpecials\bpsim_Data\Managed"
set "MODDIR=%PROJ%\Assets\StreamingAssets\Mods\%~1"
set "OUTDLL=%MODDIR%\%~2"

if not exist "%MCS%" (
  echo ERROR: mcs.bat not found: %MCS%
  echo Edit MCS in this script to point at your Unity editor install.
  exit /b 1
)
if not exist "%MANAGED%\Assembly-CSharp.dll" (
  echo ERROR: reference assemblies not found: %MANAGED%\Assembly-CSharp.dll
  echo Run a player build first, e.g.
  echo   Unity -executeMethod Bpsim.EditorTools.WindowsPlayerBuild.BuildDefault
  exit /b 1
)
if not exist "%MODDIR%\Code" (
  echo ERROR: no Code folder in %MODDIR%
  exit /b 1
)

echo Compiling %~1 -^> %~2
call "%MCS%" -target:library -langversion:latest -nostdlib+ -noconfig -out:"%OUTDLL%" ^
  -r:"%MANAGED%\mscorlib.dll" ^
  -r:"%MANAGED%\netstandard.dll" ^
  -r:"%MANAGED%\System.dll" ^
  -r:"%MANAGED%\System.Core.dll" ^
  -r:"%MANAGED%\Unity.Entities.dll" ^
  -r:"%MANAGED%\UnityEngine.CoreModule.dll" ^
  -r:"%MANAGED%\UnityEngine.dll" ^
  -r:"%MANAGED%\Assembly-CSharp.dll" ^
  "%MODDIR%\Code\*.cs"

if errorlevel 1 (
  echo Compilation FAILED.
  exit /b 1
)
echo Done: %OUTDLL%
exit /b 0

:usage
echo Usage: build-mod-dll.bat ^<mod-folder^> ^<Output.dll^>
echo   e.g. build-mod-dll.bat bpreboot-specials BprebootSpecials.dll
exit /b 1
