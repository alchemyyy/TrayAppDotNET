@echo off
setlocal

set "ROOT=%~dp0"

set "FOLDERS=.vs bin obj"
set "PROJECTS=BatteryTrayAppDotNET BrightnessTrayAppDotNET FanControlTrayAppDotNET NetworkTrayAppDotNET TrayAppDotNETCommon VolumeTrayAppDotNET"

for %%F in (%FOLDERS%) do (
    call :DeleteFolder "%%F"
)

for %%P in (%PROJECTS%) do (
    for %%F in (%FOLDERS%) do (
        call :DeleteFolder "%%P\%%F"
    )
)

exit /b 0

:DeleteFolder
set "TARGET=%ROOT%%~1"

if exist "%TARGET%\" (
    echo Deleting "%TARGET%"
    rmdir /s /q "%TARGET%"
) else (
    echo Skipping missing "%TARGET%"
)

exit /b 0
