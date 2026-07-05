# TrayAppDotNET Project Map

## Solution

- Root solution: `TrayAppDotNET.slnx`
- Platform: x64
- Shared project: `TrayAppDotNETCommon/src/TrayAppDotNETCommon.csproj`
- XML source generator:
  - `TrayAppDotNETCommon/generators/XmlSourceGenerator/TrayAppDotNETCommon.XmlSourceGenerator.csproj`
  - `TrayAppDotNETCommon/tests/XmlSourceGenerator.Tests/TrayAppDotNETCommon.XmlSourceGenerator.Tests.csproj`
- AXAML property-linker source generator:
  - `TrayAppDotNETCommon/generators/AxamlPropertyLinker/TrayAppDotNETCommon.AxamlPropertyLinker.csproj`
  - `TrayAppDotNETCommon/tests/AxamlPropertyLinker.Tests/TrayAppDotNETCommon.AxamlPropertyLinker.Tests.csproj`
- Apps:
  - `BatteryTrayAppDotNET/src/BatteryTrayAppDotNET.csproj`
  - `BrightnessTrayAppDotNET/src/BrightnessTrayAppDotNET.csproj`
  - `FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj`
  - `NetworkTrayAppDotNET/src/NetworkTrayAppDotNET.csproj`
  - `VolumeTrayAppDotNET/src/VolumeTrayAppDotNET.csproj`
- Tests:
  - `BrightnessTrayAppDotNET/tests/BrightnessTrayAppDotNET.Tests`
  - `FanControlTrayAppDotNET/tests/FanControlTrayAppDotNET.Tests`

## Shared Infrastructure

- Common tray shell and Win32 notification icon:
  - `TrayAppDotNETCommon/src/UI/Tray/TrayAppDotNETShellTrayIcon.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/NativeIcon.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/TrayIconRenderer.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/TrayIconRenderQueue.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/TrayMenuWindow.cs`
- Common Avalonia startup and rendering:
  - `TrayAppDotNETCommon/src/UI/TrayAppDotNETAvalonia.cs`
  - App-specific `src/App.cs`
  - App-specific `src/Program.cs`
- Common settings and controls:
  - `TrayAppDotNETCommon/src/UI/SettingsWindowCommon.cs`
  - `TrayAppDotNETCommon/src/UI/CommonBindings.cs`
  - `TrayAppDotNETCommon/src/UI/Settings`
  - `TrayAppDotNETCommon/src/UI/Controls`
  - `TrayAppDotNETCommon/src/UI/Controls/FlyoutSlider.cs`
  - `TrayAppDotNETCommon/src/UI/Controls/SearchableListBox.cs`
- AXAML resource readers:
  - `TrayAppDotNETCommon/src/UI/HotReloadResourceReader.cs`
  - `TrayAppDotNETCommon/src/UI/TrayAppDotNETAXAMLResources.cs`
  - `TrayAppDotNETCommon/src/UI/Controls/ControlAXAMLResources.cs`
- Time constants and throttling:
  - `TrayAppDotNETCommon/src/TimeConstants.cs`
  - `TrayAppDotNETCommon/src/Services/AsyncThrottler.cs`
- Install/update/startup:
  - `TrayAppDotNETCommon/src/ProgramStartup.cs`
  - `TrayAppDotNETCommon/src/Services/UpdateCheckService.cs`
  - `TrayAppDotNETCommon/src/Services/Install`
  - `TrayAppDotNETCommon/src/Services/WatcherMonitor.cs`

## App Hotspots

### Volume

- App startup/lifetime: `VolumeTrayAppDotNET/src/App.cs`, `VolumeTrayAppDotNET/src/Program.cs`
- Flyout and slider behavior: `VolumeTrayAppDotNET/src/UI/Flyout`
- Tray icon behavior: `VolumeTrayAppDotNET/src/UI/Tray/VolumeTrayIcon.cs`
- Audio backend: `VolumeTrayAppDotNET/src/Audio`
- Common freeze path: audio callbacks, flyout updates, tray shell updates, tooltip sync, and raw input.

### Brightness

- App startup/lifetime: `BrightnessTrayAppDotNET/src/App.cs`, `BrightnessTrayAppDotNET/src/Program.cs`
- Tray icon behavior: `BrightnessTrayAppDotNET/src/Visuals/BrightnessTrayIcon.cs`
- Environmental/curve behavior: `BrightnessTrayAppDotNET/src/Services`, `BrightnessTrayAppDotNET/src/UI/Settings`
- Tests: `BrightnessTrayAppDotNET/tests/BrightnessTrayAppDotNET.Tests`

### Fan Control

- App startup/lifetime: `FanControlTrayAppDotNET/src/App.cs`, `FanControlTrayAppDotNET/src/Program.cs`
- Flyout and fan cards: `FanControlTrayAppDotNET/src/UI/Flyout`
- Settings: `FanControlTrayAppDotNET/src/UI/Settings`
- Curve editor: `FanControlTrayAppDotNET/src/UI/Curves`
- Hardware service and models: `FanControlTrayAppDotNET/src/Services`, `FanControlTrayAppDotNET/src/Models`
- LibreHardwareMonitor source submodule:
  - `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib`
  - AMD CPU CCD work centers on `LibreHardwareMonitorLib/Hardware/Cpu/Amd17Cpu.cs`
- Tests: `FanControlTrayAppDotNET/tests/FanControlTrayAppDotNET.Tests`

### Network

- App startup/lifetime: `NetworkTrayAppDotNET/src/App.cs`, `NetworkTrayAppDotNET/src/Program.cs`
- Network monitor: `NetworkTrayAppDotNET/src/Services/NetworkMonitor.cs`
- Settings pages: `NetworkTrayAppDotNET/src/UI/Settings`
- Network may intentionally differ from other apps for rendering/backend behavior.

### Battery

- App startup/lifetime: `BatteryTrayAppDotNET/src/App.cs`, `BatteryTrayAppDotNET/src/Program.cs`
- Battery monitor: `BatteryTrayAppDotNET/src/Services/BatteryMonitorService.cs`
- Flyout/settings: `BatteryTrayAppDotNET/src/UI`

## Build And Packaging Files

- Shared MSBuild:
  - `Directory.Build.props`
  - `TrayAppDotNET.Parent.props`
  - `TrayAppDotNET.Parent.targets`
- GitHub workflows:
  - `.github/workflows/*-debug.yml`
  - `.github/workflows/*-release.yml`
  - `.github/workflows/publish.yml`
  - `.github/workflows/increment-build-numbers.yml`
- Release mode currently uses Native AOT in app project files when `RuntimeIdentifier` is set:
  - `SelfContained=true`
  - `PublishAot=true`
  - `PublishSingleFile=false`

## Launch Model

- Running an app with no arguments starts normal mode.
- Normal mode uses the crash watcher process, then the watcher starts the monitored app process.
- Useful arguments are documented in repo `README.md`:
  - `--install local`
  - `--install system`
  - `--installlocal`
  - `--installsystem`
  - `--uninstall <installDir> --scope <scope>`
  - `--watcher`
  - `--monitored --watcher-pid <pid>`

## Repeated Discovery To Avoid

- Do not rediscover the same topology with full-repo scans if the task clearly maps to the sections above.
- Start from the named app plus `TrayAppDotNETCommon`.
- For build/release tasks, inspect `.csproj`, `.props`, `.targets`, workflows, and publish scripts early.
