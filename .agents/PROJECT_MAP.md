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
  - `TaskManagerTrayAppDotNET/src/TaskManagerTrayAppDotNET.csproj`
  - `VolumeTrayAppDotNET/src/VolumeTrayAppDotNET.csproj`
- Installer factory (single-file .NET Framework 4.8 WPF app; deliberately does not reference TrayAppDotNETCommon):
  - `TrayAppDotNETInstaller/src/TrayAppDotNETInstaller.csproj`
  - `TrayAppDotNETInstaller/src/Services/SolidPayloadArchive.cs`
  - `TrayAppDotNETInstaller/src/Services/ExampleMode.cs`
  - `TrayAppDotNETInstaller/src/Properties/launchSettings.json`
  - `TrayAppDotNETInstaller/src/Compression/LzmaConstants.cs`
  - `TrayAppDotNETInstaller/src/Compression/LzmaEncoder.cs`
  - `TrayAppDotNETInstaller/src/Compression/LzmaDecoder.cs`
  - `TrayAppDotNETInstaller/tests/TrayAppDotNETInstaller.Tests`
  - `TrayAppDotNETInstaller/tests/TrayAppDotNETInstaller.Tests/LzmaRoundTripTests.cs`
  - `TrayAppDotNETInstaller/tests/TrayAppDotNETInstaller.Tests/LzmaConformanceTests.cs`
  - Built once per release with no payload. Running that binary with `--make-installer` copies itself,
    replaces the shell icon through the Win32 resource APIs, repacks each app package zip into a solid
    payload, and appends those, producing `Installer_<App>_<version>.exe`. Payloads live after the PE image behind a
    trailer, so only one Native AOT compilation is needed no matter how many installers a release ships.
  - A solid payload is a 72 byte header (magic `TADNSLD1`), an uncompressed entry table, then one LZMA1
    stream over the concatenated file data of every entry. A zip compresses each entry separately through a
    32 KB DEFLATE window; the single stream uses a dictionary of up to 32 MB, so matches reach across the
    whole package. Appended entries are named `<App>_<Version>.tadn` rather than `<App>_<Version>.zip`.
  - The LZMA encoder and decoder under `src/Compression` are hand written and conformant LZMA1: liblzma
    decodes what the encoder writes, and the decoder reads liblzma streams. `LzmaRoundTripTests` and
    `LzmaConformanceTests` cover both directions.
  - `--verify-installer --image <path>` opens the payload archive appended to an image, decompresses every
    payload in full, and checks each against the SHA-256 recorded in its header. `--make-installer` runs the
    same check before publishing its output, so no installer is written without one proven decode pass.
    `.github/scripts/publish.py` calls `--verify-installer` after stamping.
  - Measured on the VolumeTrayAppDotNET release package: 50,329,628 raw bytes, 20,222,033 as a DEFLATE zip,
    16,899,152 as a solid payload. The stamped installer went from 21,207,583 to 17,202,910 bytes, an 18.9
    percent reduction. Extracting the payload at install time takes about 1.1 seconds.
  - A package too large to hold resident while packing, over 512 MB uncompressed, is appended as its original
    zip instead, and the installer still reads it.
  - The project is runnable on its own for interface work. `--example [App]` fabricates a catalog from the
    embedded application icons and swaps the engine for a simulation that walks the same progress stages
    while extracting nothing, starting no child installer and writing nothing; `--example-fail` makes that
    simulation fail partway, which is the only way to reach the failure state. The example window also
    reports no Windhawk and no battery, so the Windhawk notice and the battery application unselecting
    itself are both visible. It also fabricates existing installations
    (`ExampleMode.CreateDetectedInstallations`), so the installed notice, the row labels and the preset
    installation type show. `launchSettings.json` carries the profiles, so `dotnet run` and the IDE run
    button need no arguments, and a Debug build with no payload falls into the same mode. A stamped
    installer is a Release build carrying a payload, so it cannot reach any of this; `ExampleModeTests`
    covers the argument parsing, the fabricated catalog and the simulated run.
  - The window draws its own title bar through WPF's `WindowChrome`, because the one Windows draws cannot be
    made taller for a single window. It is 34 units, 4 more than the 30 Windows gives this non-resizable
    window, and every size and colour is in `Theme.xaml`. The icon draws from the frame nearest its size at
    the current display scaling, and clicking it opens the system menu.
  - Targets `net48` on purpose. The framework and its renderer ship with Windows, so the installer is a few
    hundred kilobytes instead of the 28 MB an Avalonia Native AOT build cost, which embedded Skia.
  - `src/GlobalUsings.cs` supplies the usings the SDK only provides implicitly on .NET 6 and newer, and
    `src/Compatibility` holds the compiler attribute polyfills and stand-ins for missing .NET APIs.
  - Every app icon plus the suite icon is embedded in the factory, so the window icon needs no surgery. The
    suite list draws each app's icon before its name as a mask in the row's text colour, because the icons are
    white line art that vanishes on the light palette.
  - `src/Services/InstallationDetector.cs` finds existing Local and System copies by the executable in the
    folder each mode installs to; the uninstall registry's build number goes stale after in-app updates, so it
    is not read, and portable copies leave no record. The window starts on the mode holding the most copies
    (System on a tie), shows an informational notice, and labels suite rows `Installed (System)` and so on.
  - After a successful install the window launches each app with `--hidden` through `AppLauncher`. A process
    elevated under UAC (`SystemProbes.IsSplitTokenElevated`) launches through the desktop Explorer's
    `IShellDispatch2.ShellExecute` (`UnelevatedLauncher`), which keeps the arguments; `explorer.exe <path>`
    is only the fallback because it drops them. With UAC off the installer launches directly.
  - `dotnet run` with no payload falls back to a `Payloads` folder beside the executable, which still holds
    plain `.zip` files. `ExtractArchive` in `src/Services/InstallEngine.cs` tells a solid payload from a zip
    by the leading bytes, not the file name.
  - A Release build of an app stamps that app's installer beside it through the shared
    `StampAppInstallerAfterBuild` target in `TrayAppDotNET.Parent.targets`. The LZMA pass puts that at about
    11 seconds per installer. Pass `-p:TrayAppDotNETStampAppInstaller=false` to skip it during iterative
    Release work.
- Tests:
  - `BrightnessTrayAppDotNET/tests/BrightnessTrayAppDotNET.Tests`
  - `FanControlTrayAppDotNET/tests/FanControlTrayAppDotNET.Tests`
  - `VolumeTrayAppDotNET/tests/VolumeTrayAppDotNET.Tests`

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
  - Progress wire protocol shared with the installer app: `TrayAppDotNETCommon/src/Services/Install/InstallProgress.cs`
  - Elevated helper progress pipe: `TrayAppDotNETCommon/src/Services/Install/ProgressPipe.cs`
  - `TrayAppDotNETCommon/src/Services/UpdateCheckService.cs`
  - `TrayAppDotNETCommon/src/Services/Install`
  - `TrayAppDotNETCommon/src/Services/WatcherMonitor.cs`

## App Hotspots

### Volume

- App startup/lifetime: `VolumeTrayAppDotNET/src/App.cs`, `VolumeTrayAppDotNET/src/Program.cs`
- Flyout and slider behavior: `VolumeTrayAppDotNET/src/UI/Flyout`
- Tray icon behavior: `VolumeTrayAppDotNET/src/UI/Tray/VolumeTrayIcon.cs`
- Audio backend: `VolumeTrayAppDotNET/src/Audio`
- Tests: `VolumeTrayAppDotNET/tests/VolumeTrayAppDotNET.Tests`
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

### Task Manager

- App startup/lifetime: `TaskManagerTrayAppDotNET/src/App.cs`, `TaskManagerTrayAppDotNET/src/Program.cs`
- Shared settings-shell derivative: `TaskManagerTrayAppDotNET/src/UI/TaskManagerWindow.cs`
- Painted Details table: `TaskManagerTrayAppDotNET/src/UI/ProcessDetailsCanvas.cs`
- Render-thread cursor-sampled row hover: `TaskManagerTrayAppDotNET/src/UI/ProcessRowHoverVisual.cs`
- Fixed-buffer process sampler: `TaskManagerTrayAppDotNET/src/Services/ProcessSnapshotService.cs`
- Processes Status column, mirrored from Taskmgr.exe: `TaskManagerTrayAppDotNET/src/Models/ProcessStatus.cs`,
  `TaskManagerTrayAppDotNET/src/Services/ProcessHungWindowCollector.cs`; rules in
  `documentation/docs/task_manager_process_status.md`
- Tests: `TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests`

## Build And Packaging Files

- Shared MSBuild:
  - `Directory.Build.props`
  - `TrayAppDotNET.Parent.props`
  - `TrayAppDotNET.Parent.targets`
- GitHub workflows:
  - `.github/workflows/*-debug.yml`
  - `.github/workflows/*-release.yml`
  - `.github/workflows/publish.yml`
- Release packaging script (app zips, then stamping per-app `Installer_<App>_<version>.exe` and the suite
  `Installer_TrayAppDotNET_<version>.exe` from one factory build, then `--verify-installer` on each stamped image):
  `.github/scripts/publish.py`
- App icons and the installer icon are generated by `tools/AppIconGenerator` (target `TADN` writes `TrayAppDotNETInstaller/app.ico`)
- Release mode currently uses Native AOT in app project files when `RuntimeIdentifier` is set:
  - `SelfContained=true`
  - `PublishAot=true`
  - `PublishSingleFile=false`

## Launch Model

- Running an app with no arguments starts normal mode.
- Normal mode uses the crash watcher process, then the watcher starts the monitored app process.
- The Run on startup shortcut passes `--autostart`; the watcher forwards it to the monitored process, where
  `TrayAppDotNETProgram.IsStartupLaunch` exposes it. Task Manager's Start minimized option only applies then.
- The installer passes `--hidden` to the apps it launches; `TrayAppDotNETProgram.IsHiddenLaunch` exposes it and
  Task Manager then starts in the tray without showing its window. The watcher rebuilds the monitored command
  line, so only the flags in `TrayAppDotNETProgram.ForwardedLaunchArguments` survive it.
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
