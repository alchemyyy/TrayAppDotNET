# BatteryTrayAppDotNET

Avalonia ReadyToRun Windows tray app for battery status.

This rewrite is based on `VolumeTrayAppDotNET` for the TrayAppDotNET shell, but the app logic is battery-specific and does not use LibreHardwareMonitor. Battery state comes from the Windows power manager:

* `GetSystemPowerStatus` for shell-visible percentage, AC/DC state, charging flag, and Windows' time estimate.
* `Windows.Devices.Power.BatteryReport` for charge rate and battery capacity fields.

## Features

* Native tray icon with level-aware battery/charging glyphs.
* Left-click battery flyout with charge, status, power rate, capacity, and health.
* Right-click menu for Power Options, Battery Report, settings folder, and exit.
* Install/update/uninstall plumbing from `TrayAppDotNETCommon`.

## Build

```powershell
dotnet build .\BatteryTrayAppDotNET.slnx -c Debug
dotnet build .\BatteryTrayAppDotNET.slnx -c Release
```

Release builds publish a self-contained ReadyToRun app-relative layout and increment `buildnumber.txt`.
