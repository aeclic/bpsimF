@echo off
rem ============================================================================
rem  unity-qa.bat - run one of this project's play-mode QA drivers in batch.
rem
rem  USAGE
rem    Tools\unity-qa.bat LayerConnection   -> Bpsim.EditorTools.LayerConnectionQaEditor.RunBatch
rem    Tools\unity-qa.bat FreeTransform     -> Bpsim.EditorTools.FreeTransformQaEditor.RunBatch
rem    Tools\unity-qa.bat BpirImport        -> Bpsim.EditorTools.BpirImportQaEditor.RunBatch
rem    Tools\unity-qa.bat BuildToolbar      -> Bpsim.EditorTools.BuildToolbarQaEditor.RunBatch
rem
rem  WHY NOT unity-batch.bat
rem  -----------------------
rem  unity-batch.bat passes -quit, which makes Unity exit as soon as
rem  -executeMethod returns - before play mode ever starts. These drivers run
rem  play mode themselves and call EditorApplication.Exit(<code>) when done,
rem  so -quit must NOT be passed.
rem
rem  Reports land in Logs\{bpsim-<name>qa.log, unity-batch.log}.
rem ============================================================================
setlocal

set "SUITE=%~1"
if "%SUITE%"=="" (
  echo [unity-qa] usage: Tools\unity-qa.bat ^<LayerConnection^|FreeTransform^|BpirImport^|BuildToolbar^>
  exit /b 1
)

set "METHOD="
if /i "%SUITE%"=="LayerConnection" set "METHOD=Bpsim.EditorTools.LayerConnectionQaEditor.RunBatch"
if /i "%SUITE%"=="FreeTransform"   set "METHOD=Bpsim.EditorTools.FreeTransformQaEditor.RunBatch"
if /i "%SUITE%"=="BpirImport"      set "METHOD=Bpsim.EditorTools.BpirImportQaEditor.RunBatch"
if /i "%SUITE%"=="BuildToolbar"    set "METHOD=Bpsim.EditorTools.BuildToolbarQaEditor.RunBatch"
if "%METHOD%"=="" (
  echo [unity-qa] unknown suite: %SUITE%
  exit /b 1
)

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
  echo [unity-qa] Unity editor not found: "%UNITY%"
  echo [unity-qa] Set UNITY_EXE to the editor you want to use.
  exit /b 1
)

pushd "%~dp0.."
set "PROJ=%CD%"
if not exist "%PROJ%\Logs" mkdir "%PROJ%\Logs"
set "LOG=%PROJ%\Logs\unity-batch.log"
if exist "%LOG%" del "%LOG%"

echo [unity-qa] suite  : %SUITE% (%METHOD%)
echo [unity-qa] editor : %UNITY%
echo [unity-qa] project: %PROJ%

"%UNITY%" -batchmode -projectPath "%PROJ%" -logFile "%LOG%" -executeMethod %METHOD%
set "RC=%ERRORLEVEL%"
popd

echo [unity-qa] %SUITE% exited with %RC%
endlocal & exit /b %RC%
