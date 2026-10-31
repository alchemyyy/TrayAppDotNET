# BatteryTrayAppDotNET

A tray battery monitor for laptops, part of [TrayAppDotNET](../README.md).

- Tray icon with a level and charging glyph (grayed out when no battery is present).
- A flyout with the charge, a status line, charge or discharge rate in watts, remaining capacity, battery health,
  time estimates (predicted life from learned usage, present discharge, time until full), a power mode slider and an
  Energy Saver button.
- A tray menu with Power options and a battery report (`powercfg /batteryreport`).
- Three configurable triggers (condition plus action) and hotkey actions.

Battery data comes from the Windows power manager (`GetSystemPowerStatus` and `Windows.Devices.Power.BatteryReport`);
the app does not use LibreHardwareMonitor.

User documentation: [documentation/docs/apps/battery](../documentation/docs/apps/battery/index.md).

## Building

Build instructions, tooling and the per-app solution files are described in the [root README](../README.md#development).
Release builds publish Native AOT; see `.agents/PROJECT_MAP.md` for the build files.
