@echo off
setlocal

set "ROOT=%~dp0"

set "FOLDERS=.vs bin obj"
set "PROJECTS=BrightnessTrayAppDotNET\tests\NightLightTester FanControlTrayAppDotNET\tests\FanControlTrayAppDotNET.Tests VolumeTrayAppDotNET\tests\VolumeTrayAppDotNET.Tests BrightnessTrayAppDotNET\tests\BrightnessTrayAppDotNET.Tests BrightnessTrayAppDotNET\tests\BrightnessTrayAppDotNET.Tests TaskManagerTrayAppDotNET\tests\TaskManagerTrayAppDotNET.Tests TrayAppDotNETCommon\tests\XmlSourceGenerator.Tests TrayAppDotNETCommon\tests\AxamlPropertyLinker.Tests TrayAppDotNETCommon\generators\XmlSourceGenerator TrayAppDotNETCommon\generators\AxamlPropertyLinker BatteryTrayAppDotNET BrightnessTrayAppDotNET FanControlTrayAppDotNET NetworkTrayAppDotNET TaskManagerTrayAppDotNET TrayAppDotNETCommon VolumeTrayAppDotNET"

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
