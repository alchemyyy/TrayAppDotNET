@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "SCOPE=%~1"

if "%SCOPE%"=="" set "SCOPE=local"

if /I "%SCOPE%"=="local" (
    set "INSTALL_ARG=--installlocal"
) else if /I "%SCOPE%"=="system" (
    set "INSTALL_ARG=--installsystem"
) else (
    echo Usage: %~nx0 [local^|system]
    exit /b 2
)

set "EXIT_CODE=0"

call :InstallApp "BatteryTrayAppDotNET.exe"
call :InstallApp "BrightnessTrayAppDotNET.exe"
call :InstallApp "FanControlTrayAppDotNET.exe"
call :InstallApp "NetworkTrayAppDotNET.exe"
call :InstallApp "VolumeTrayAppDotNET.exe"

if not "%EXIT_CODE%"=="0" (
    echo One or more TrayAppDotNET app installs failed.
    exit /b %EXIT_CODE%
)

echo All TrayAppDotNET apps installed.
exit /b 0

:InstallApp
set "APP=%~1"
set "EXE=%ROOT%%APP%"

if not exist "%EXE%" (
    echo Missing %APP%.
    if "%EXIT_CODE%"=="0" set "EXIT_CODE=1"
    exit /b 0
)

echo Installing %APP% to %SCOPE%...
start "" /wait "%EXE%" %INSTALL_ARG%
if errorlevel 1 (
    echo %APP% failed with exit code %ERRORLEVEL%.
    if "%EXIT_CODE%"=="0" set "EXIT_CODE=%ERRORLEVEL%"
) else (
    echo %APP% installed.
)

exit /b 0
