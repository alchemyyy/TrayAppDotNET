@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem Deletes every bin\, obj\ and .vs\ folder in the repository, so the list never goes stale when a project is
rem added or moved. Vendored submodules keep their own outputs, and tool-managed folders are left alone.
set "ROOT=%~dp0"

for /d /r "%ROOT%" %%D in (bin obj .vs) do (
    set "TARGET=%%D"
    if exist "!TARGET!\" call :DeleteUnlessExcluded
)

exit /b 0

:DeleteUnlessExcluded
for %%X in (\FanControlTrayAppDotNET\vendor\ \node_modules\ \.git\ \.venv\) do (
    if /I not "!TARGET:%%X=!"=="!TARGET!" exit /b 0
)

echo Deleting "!TARGET!"
rmdir /s /q "!TARGET!"
exit /b 0
