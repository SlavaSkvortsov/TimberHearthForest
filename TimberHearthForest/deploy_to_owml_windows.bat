@echo off
setlocal EnableExtensions

REM ============================================================
REM Timber Hearth Forest - Windows deploy helper
REM ------------------------------------------------------------
REM Copies only selected/changed files into your OWML mod install.
REM Run this .bat from Windows after building the DLL.
REM ============================================================

REM ---- Destination from your message (edit if needed) ----
set "TARGET_ASSETS_DIR=C:\Users\i_hat\AppData\Roaming\OuterWildsModManager\OWML\Mods\GameDev46.TimberHearthForest\Assets"
set "TARGET_MOD_DIR=%TARGET_ASSETS_DIR%\.."

REM ---- Source (folder where this .bat lives) ----
set "SRC_DIR=%~dp0"

REM ---- Toggle optional asset copies (1=yes, 0=no) ----
set "COPY_TREE_SPAWN=1"
set "COPY_GENERATOR_SCRIPT=1"
set "COPY_GENERATED_FILES=1"

echo.
echo [INFO] Source mod folder:      "%SRC_DIR%"
echo [INFO] Target mod folder:      "%TARGET_MOD_DIR%"
echo [INFO] Target assets folder:   "%TARGET_ASSETS_DIR%"
echo.

if not exist "%TARGET_MOD_DIR%\" (
  echo [ERROR] Target mod directory does not exist:
  echo         "%TARGET_MOD_DIR%"
  exit /b 1
)

if not exist "%TARGET_ASSETS_DIR%\" (
  echo [INFO] Assets directory missing, creating:
  echo        "%TARGET_ASSETS_DIR%"
  mkdir "%TARGET_ASSETS_DIR%" || (
    echo [ERROR] Failed to create target assets directory.
    exit /b 1
  )
)

REM ---- Core mod files ----
call :copy_if_exists "%SRC_DIR%manifest.json" "%TARGET_MOD_DIR%\manifest.json"
call :copy_if_exists "%SRC_DIR%default-config.json" "%TARGET_MOD_DIR%\default-config.json"
call :copy_if_exists "%SRC_DIR%bin\Release\TimberHearthForest.dll" "%TARGET_MOD_DIR%\TimberHearthForest.dll"

REM ---- Assets: only selected files ----
if "%COPY_TREE_SPAWN%"=="1" (
  call :copy_if_exists "%SRC_DIR%Assets\treeSpawnData.json" "%TARGET_ASSETS_DIR%\treeSpawnData.json"
)

if "%COPY_GENERATOR_SCRIPT%"=="1" (
  call :copy_if_exists "%SRC_DIR%Assets\augment_tree_spawns.py" "%TARGET_ASSETS_DIR%\augment_tree_spawns.py"
)

if "%COPY_GENERATED_FILES%"=="1" (
  call :copy_if_exists "%SRC_DIR%Assets\treeSpawnData.generated.json" "%TARGET_ASSETS_DIR%\treeSpawnData.generated.json"
  call :copy_if_exists "%SRC_DIR%Assets\treeSpawnData.original.json" "%TARGET_ASSETS_DIR%\treeSpawnData.original.json"
)

echo.
echo [DONE] Deploy finished.
echo        If DLL was skipped, build Release first in Visual Studio or with dotnet.
exit /b 0


:copy_if_exists
set "SRC_FILE=%~1"
set "DST_FILE=%~2"

if not exist "%SRC_FILE%" (
  echo [WARN] Missing source, skipped: "%SRC_FILE%"
  goto :eof
)

copy /Y "%SRC_FILE%" "%DST_FILE%" >nul
if errorlevel 1 (
  echo [ERROR] Copy failed:
  echo        from "%SRC_FILE%"
  echo        to   "%DST_FILE%"
) else (
  echo [OK] Copied from "%SRC_FILE%" to "%DST_FILE%"
)
goto :eof
