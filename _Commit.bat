@echo off
setlocal enabledelayedexpansion

if "%~1"=="" ( set "LOG_DIR=C:\Temp\CommitLogs" ) else ( set "LOG_DIR=%~1" )
if "%~2"=="" ( set "LOG_FILE=commit_default.log" ) else ( set "LOG_FILE=%~2" )
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

set "WORK_DIR=%~dp0"
cd /d "%WORK_DIR%"

echo "%LOG_FILE%" | findstr /i ":" >nul
if not errorlevel 1 (
    set "LOG_FULL_PATH=%LOG_FILE%"
) else (
    set "LOG_FULL_PATH=%LOG_DIR%\%LOG_FILE%"
)

echo %DATE% %TIME% Committing %WORK_DIR%...
echo %DATE% %TIME% Committing %WORK_DIR%...>>"%LOG_FULL_PATH%"

git add -A
if errorlevel 1 ( echo %DATE% %TIME% ERROR: git add failed>>"%LOG_FULL_PATH%" & exit /b 1 )
if "%*"=="" (
    git commit -m "Auto-commit" >nul 2>&1
) else (
    git commit -m "%*" >nul 2>&1
)

git push --force-with-lease origin >nul 2>&1 || git push origin >nul 2>&1 || git push --force >nul 2>&1 || git push >nul 2>&1 || ver >nul

if exist "C:\Batch\bin\CodeScrapper.exe" (
    C:\Batch\bin\CodeScrapper.exe -bl-actions="hyrdateCodebaseWarehouseFolders" -folders="%CD%" >nul 2>&1
)

echo %DATE% %TIME% Commit complete for %WORK_DIR%
echo %DATE% %TIME% Commit complete for %WORK_DIR%>>"%LOG_FULL_PATH%"
exit /b 0
