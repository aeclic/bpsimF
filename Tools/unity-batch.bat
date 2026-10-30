@echo off
rem ============================================================================
rem  unity-batch.bat - run this project's Editor in batch mode, safely.
rem
rem  WHY THIS EXISTS
rem  ---------------
rem  Unity's Bee build backend (ScriptCompilationBuildProgram.exe) builds a
rem  case-insensitive dictionary from the process environment. If the
rem  environment contains BOTH cases of a proxy variable - e.g. "NO_PROXY" and
rem  "no_proxy" (launchers/agents such as DSH inject both variants) - it dies
rem  with:
rem
rem    Internal build system error. BuildProgram exited with code -532462766.
rem    System.ArgumentException: Item has already been added.
rem      Key in dictionary: 'NO_PROXY'  Key being added: 'no_proxy'
rem      at Bee.Stevedore.Program.EnvironmentPaths..ctor(...)
rem
rem  and Unity then reports "Scripts have compiler errors." even though the C#
rem  compiler never ran. Clearing the duplicate proxy variables in this cmd
rem  environment (cmd's SET is case-insensitive) makes the child Editor see one
rem  or zero variants, which unblocks compilation.
rem
rem  USAGE
rem  -----
rem    Tools\unity-batch.bat                                  (compile + quit)
rem    Tools\unity-batch.bat -executeMethod My.Editor.Task
rem    set UNITY_EXE=<other editor>\Editor\Unity.exe & Tools\unity-batch.bat
rem ============================================================================
setlocal

rem Drop every case-variant of the proxy variables; %* is forwarded to Unity.
set no_proxy=
set http_proxy=
set https_proxy=
set NO_PROXY=
set HTTP_PROXY=
set HTTPS_PROXY=
set NODE_USE_ENV_PROXY=

set "UNITY=%UNITY_EXE%"
if "%UNITY%"=="" set "UNITY=D:\folder\2022.2.23f1\Editor\Unity.exe"

if not exist "%UNITY%" (
  echo [unity-batch] Unity editor not found: "%UNITY%"
  echo [unity-batch] Set UNITY_EXE to the editor you want to use.
  exit /b 1
)

pushd "%~dp0.."
set "PROJ=%CD%"
if not exist "%PROJ%\Logs" mkdir "%PROJ%\Logs"
set "LOG=%PROJ%\Logs\unity-batch.log"

echo [unity-batch] editor : %UNITY%
echo [unity-batch] project: %PROJ%
echo [unity-batch] log    : %LOG%

"%UNITY%" -batchmode -quit -projectPath "%PROJ%" -logFile "%LOG%" %*
set "RC=%ERRORLEVEL%"
popd

echo [unity-batch] Unity exited with %RC%
endlocal & exit /b %RC%
