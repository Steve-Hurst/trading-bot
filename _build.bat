@echo off
setlocal enabledelayedexpansion

if "%~1"=="" ( set "LOG_DIR=C:\Temp\BuildLogs" ) else ( set "LOG_DIR=%~1" )
if "%~2"=="" ( set "LOG_FILE=build_cookie.autotrader-bot01.log" ) else ( set "LOG_FILE=%~2" )
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

set "PROJ_DIR=%~dp0"
set "APP_NAME=cookie.autotrader-bot01"
set "BIN_DIR=%PROJ_DIR%bin"

echo %DATE% %TIME% Extracting Git metadata for %APP_NAME%...
echo %DATE% %TIME% Extracting Git metadata for %APP_NAME%...>>"%LOG_DIR%\%LOG_FILE%"

cd /d "%PROJ_DIR%"

set "GIT_SHA=unknown"
for /f "tokens=*" %%a in ('git rev-parse HEAD 2^>nul') do set "GIT_SHA=%%a"

set "GIT_BRANCH=unknown"
for /f "tokens=*" %%b in ('git branch --show-current 2^>nul') do set "GIT_BRANCH=%%b"

set "GIT_LABEL=unknown"
for /f "tokens=*" %%c in ('git describe --tags --always 2^>nul') do set "GIT_LABEL=%%c"

set "BUILD_DATE=%DATE% %TIME%"
set "COOKIE_CONTROL_TOKEN=0000000000000000000000000000000000000000000000000000000000000000"

echo Requesting build token from Cookie-Control Vault (http://localhost:9500/build?appName=%APP_NAME%)...
for /f "tokens=*" %%t in ('powershell -Command "try { (Invoke-RestMethod -Uri 'http://localhost:9500/build?appName=%APP_NAME%' -TimeoutSec 3).token } catch { '' }" 2^>nul') do (
    if not "%%t"=="" set "COOKIE_CONTROL_TOKEN=%%t"
)

echo Generating AutoTraderBot01\buildinfo.cs (SHA: !GIT_SHA!, Branch: !GIT_BRANCH!, Control Token: !COOKIE_CONTROL_TOKEN!)...

(
echo // Auto-generated buildinfo for %APP_NAME%
echo using System;
echo.
echo namespace Config
echo {
echo     public static class BuildInfo
echo     {
echo         public const string AppName = "%APP_NAME%";
echo         public const string AlgorithmName = "EURUSD_MeanReversion_RSI_BB";
echo         public const string Version = "1.0.0.0";
echo         public const string BuildDate = "!BUILD_DATE!";
echo         public const string GitCommitSha = "!GIT_SHA!";
echo         public const string GitBranch = "!GIT_BRANCH!";
echo         public const string GitLabel = "!GIT_LABEL!";
echo         public const string CookieControlToken = "!COOKIE_CONTROL_TOKEN!";
echo         public const int DefaultPort = 9011;
echo         public const string ServiceName = "%APP_NAME%";
echo         public const string ServiceDescription = "Steve Hurst AutoTrader Bot 01 - EURUSD Mean Reversion Pepperstone Execution Engine";
echo     }
echo }
) > "%PROJ_DIR%AutoTraderBot01\buildinfo.cs"

echo %DATE% %TIME% Building %APP_NAME%...
echo %DATE% %TIME% Building %APP_NAME%...>>"%LOG_DIR%\%LOG_FILE%"

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
