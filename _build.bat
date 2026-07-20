@echo off
setlocal enabledelayedexpansion

if "%~1"=="" ( timeout /t 30 /nobreak >nul & set "LOG_DIR=C:\Temp\BuildLogs" ) else ( set "LOG_DIR=%~1" )
if "%~2"=="" ( timeout /t 30 /nobreak >nul & set "LOG_FILE=build_default.log" ) else ( set "LOG_FILE=%~2" )
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

set "PROJ_DIR=%~dp0"
set bldAppName=trading-bot

echo %DATE% %TIME% Building %bldAppName%...
echo %DATE% %TIME% Building %bldAppName%...>>"%LOG_DIR%\%LOG_FILE%"

cd /d "%PROJ_DIR%"
go build -o "%PROJ_DIR%\%bldAppName%.exe" >>"%LOG_DIR%\%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo %DATE% %TIME% ERROR: go build failed>>"%LOG_DIR%\%LOG_FILE%"
    exit /b 1
)

if not exist "%PROJ_DIR%\%bldAppName%.exe" (
    echo %DATE% %TIME% ERROR: EXE NOT built>>"%LOG_DIR%\%LOG_FILE%"
    exit /b 1
)

echo %DATE% %TIME% Build complete for %bldAppName%
echo %DATE% %TIME% Build complete for %bldAppName%>>"%LOG_DIR%\%LOG_FILE%"

call C:\Batch\_deploy.bat "%LOG_DIR%" "%LOG_FILE%" "%PROJ_DIR%" "%bldAppName%.exe"
exit /b %errorlevel%
