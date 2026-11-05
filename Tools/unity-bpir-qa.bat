@echo off
rem ============================================================================
rem  unity-bpir-qa.bat - run Bpsim.QA.BpirImportQA in batch play mode.
rem
rem  WHY NOT unity-batch.bat
rem  -----------------------
rem  unity-batch.bat passes -quit, which makes Unity exit right after the
rem  -executeMethod returns - before play mode ever starts. The QA driver
rem  (Assets/Editor/BpirImportQaEditor.cs) drives play mode itself and calls
rem  EditorApplication.Exit(<code>) when done, so -quit must NOT be passed.
rem
rem  Exit code: 0 = all checks passed, 1 = failures, 2 = timeout,
rem             3 = play mode never started.
rem  Reports:   Logs\bpsim-bpirimportqa.log        (full QA report)
rem             Logs\bpsim-bpirimportqa-driver.log (driver summary)
rem             Logs\unity-batch.log               (Unity editor log)
rem ============================================================================
setlocal

rem Drop every case-variant of the proxy variables (Bee dies on duplicates).
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
  echo [unity-bpir-qa] Unity editor not found: "%UNITY%"
  echo [unity-bpir-qa] Set UNITY_EXE to the editor you want to use.
  exit /b 1
)

pushd "%~dp0.."
set "PROJ=%CD%"
if not exist "%PROJ%\Logs" mkdir "%PROJ%\Logs"
set "LOG=%PROJ%\Logs\unity-batch.log"
if exist "%LOG%" del "%LOG%"

echo [unity-bpir-qa] editor : %UNITY%
echo [unity-bpir-qa] project: %PROJ%
echo [unity-bpir-qa] log    : %LOG%

"%UNITY%" -batchmode -projectPath "%PROJ%" -logFile "%LOG%" -executeMethod Bpsim.EditorTools.BpirImportQaEditor.RunBatch
set "RC=%ERRORLEVEL%"
popd

echo [unity-bpir-qa] Unity exited with %RC%
endlocal & exit /b %RC%
