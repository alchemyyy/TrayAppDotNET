# VolumeTrayAppDotNET

A starter scaffold for Windows tray apps in Avalonia: ReadyToRun publish layout, settings window, tray icon, hotkeys, theming, localization, single-instance coordination, crash handler, and install/update/uninstall plumbing.

## What's included

* Avalonia host with a custom entry point (`Program.cs`) that wires up the crash handler before the main app spins up.
* Tray icon manager with rendered glyph icons and a settings window shell.
* Single-instance coordinator (named-pipe handoff so a second launch wakes the running instance).
* Crash handler that captures unhandled exceptions and writes a diagnostics report.
* Theming primitives (light/dark, accent colors, glyph catalog).
* Localization via standard .NET strongly-typed ResX (`Localization\Strings.resx`).
* Build-number embedding (`buildnumber.txt` is bumped once per Release publish and baked into the assembly as a `const int`).
* ReadyToRun Release publish profile (self-contained, multi-file).
* Auto-generated app icon (`_AppIconGenerator.cs`, run with the `generate-icon` CLI flag).

## What you provide

* Your actual app logic and domain services.
* Settings pages and any custom UI beyond the shell.
* Tray click / scroll / hotkey behavior specific to your app.
* Any platform interop your app needs.

## Build

```
dotnet build -c Debug
dotnet publish -r win-x64 -c Release
```

Debug is the default workflow; Release runs the full ReadyToRun publish chain and bumps `buildnumber.txt`.

## Credit

Extracted from [BrightnessTrayAppDotNET](https://github.com/) — the brightness-control tray app this skeleton was carved out of.
