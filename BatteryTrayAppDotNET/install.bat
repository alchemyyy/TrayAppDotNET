@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "APP=BatteryTrayAppDotNET.exe"
set "EXE=%ROOT%%APP%"
set "SCOPE=%~1"

if "%SCOPE%"=="" set "SCOPE=system"

if /I "%SCOPE%"=="local" (
    set "INSTALL_ARG=--installlocal"
) else if /I "%SCOPE%"=="system" (
    set "INSTALL_ARG=--installsystem"
) else (
    echo Usage: %~nx0 [local^|system]
    exit /b 2
)

if not exist "%EXE%" (
    echo Missing %APP%.
    exit /b 1
)

echo Installing %APP% to %SCOPE%...
start "" /wait "%EXE%" %INSTALL_ARG%
if errorlevel 1 (
    echo %APP% failed with exit code %ERRORLEVEL%.
    exit /b %ERRORLEVEL%
)

echo %APP% installed.
exit /b 0
