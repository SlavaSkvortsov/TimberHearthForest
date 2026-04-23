@echo off
setlocal EnableExtensions

REM ============================================================
REM Timber Hearth Forest - Windows build helper
REM ------------------------------------------------------------
REM Builds TimberHearthForest.dll from source.
REM Optional: also run deploy_to_owml_windows.bat after build.
REM
REM After build, bin\Release\ is a flat OWML-ready folder (DLL, manifest,
REM default-config.json, Assets\). Deploy copies those core files from
REM bin\Release so your Mods\...\GameDev46.TimberHearthForest\ layout matches
REM the GitHub release artifact (flat zip, no nested bin\Release paths).
REM ============================================================

REM ---- Config ----
set "CONFIG=Release"
set "RUN_DEPLOY_AFTER_BUILD=1"

REM ---- Paths (relative to this .bat) ----
set "SCRIPT_DIR=%~dp0"
set "CSPROJ=%SCRIPT_DIR%TimberHearthForest.csproj"
set "OUT_DLL=%SCRIPT_DIR%bin\%CONFIG%\TimberHearthForest.dll"
set "DEPLOY_SCRIPT=%SCRIPT_DIR%deploy_to_owml_windows.bat"

echo.
echo [INFO] Building project:
echo        "%CSPROJ%"
echo [INFO] Configuration: %CONFIG%
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
  echo [ERROR] dotnet SDK was not found in PATH.
  echo         Install .NET SDK / build tools, then retry.
  exit /b 1
)

if not exist "%CSPROJ%" (
  echo [ERROR] Could not find project file:
  echo         "%CSPROJ%"
  exit /b 1
)

dotnet build "%CSPROJ%" -c %CONFIG%
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed.
  exit /b 1
)

echo.
if exist "%OUT_DLL%" (
  echo [OK] Build succeeded: "%OUT_DLL%"
) else (
  echo [WARN] Build reported success, but DLL was not found at:
  echo        "%OUT_DLL%"
)

if "%RUN_DEPLOY_AFTER_BUILD%"=="1" (
  echo.
  if exist "%DEPLOY_SCRIPT%" (
    echo [INFO] Running deploy script...
    call "%DEPLOY_SCRIPT%"
    if errorlevel 1 (
      echo [WARN] Deploy script returned an error.
      exit /b 1
    )
  ) else (
    echo [WARN] Deploy script not found: "%DEPLOY_SCRIPT%"
  )
)

echo.
echo [DONE] Build script finished.
exit /b 0
