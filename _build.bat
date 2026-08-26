@echo off
setlocal enabledelayedexpansion

if "%~1"=="" ( set "LOG_DIR=C:\Temp\BuildLogs" ) else ( set "LOG_DIR=%~1" )
if "%~2"=="" ( set "LOG_FILE=build_cookie.autotrader-bot01.log" ) else ( set "LOG_FILE=%~2" )
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

set "PROJ_DIR=%~dp0"
set "APP_NAME=cookie.autotrader-bot01"
set "BIN_DIR=%PROJ_DIR%bin"

echo %DATE% %TIME% Building %APP_NAME%...
echo %DATE% %TIME% Building %APP_NAME%...>>"%LOG_DIR%\%LOG_FILE%"

cd /d "%PROJ_DIR%"

dotnet publish AutoTraderBot01\AutoTraderBot01.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o "%BIN_DIR%" >>"%LOG_DIR%\%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo %DATE% %TIME% ERROR: dotnet publish failed>>"%LOG_DIR%\%LOG_FILE%"
    exit /b 1
)

if not exist "%BIN_DIR%\%APP_NAME%.exe" (
    echo %DATE% %TIME% ERROR: Executable not found at %BIN_DIR%\%APP_NAME%.exe>>"%LOG_DIR%\%LOG_FILE%"
    exit /b 1
)

echo %DATE% %TIME% Build complete for %APP_NAME%
echo %DATE% %TIME% Build complete for %APP_NAME%>>"%LOG_DIR%\%LOG_FILE%"

call C:\Batch\_deploy.bat "%LOG_DIR%" "%LOG_FILE%" "%BIN_DIR%" "%APP_NAME%.exe"
exit /b %errorlevel%
