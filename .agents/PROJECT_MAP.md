# TrayAppDotNET Project Map

## Solution

- Root solution: `TrayAppDotNET.slnx`
- Platform: x64
- Shared project: `TrayAppDotNETCommon/src/TrayAppDotNETCommon.csproj`
- XML source generator:
  - `TrayAppDotNETCommon/generators/XmlSourceGenerator/TrayAppDotNETCommon.XmlSourceGenerator.csproj`
  - `TrayAppDotNETCommon/tests/TrayAppDotNETCommon.Tests/TrayAppDotNETCommon.Tests.csproj` (generator tests plus every Common
    runtime and UI test)
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
  - `--verify-installer --image <path>` opens the payload archive appended to an image, decompresses every solid
    payload in full, and checks each against the SHA-256 recorded in its header; a zip-fallback payload is checked
    against the container hash of its archive entry only. `--make-installer` runs the same check before publishing
    its output, so no installer is written without one proven pass. `.github/scripts/publish.py` calls
    `--verify-installer` after stamping.
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
    button need no arguments, and a Debug build with no payload falls into the same mode. `--example`,
    `--example-fail` and the `Payloads` folder fallback are compiled into Debug builds only, so a stamped
    installer, which is always a Release build, cannot reach any of this; a Release build without a valid
    appended archive reports itself as damaged instead. `ExampleModeTests` covers the argument parsing, the
    fabricated catalog and the simulated run.
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
  - `InstallEngine.RunAsync` installs the selected apps concurrently, up to one per processor, and
    `InstallProgressAggregator` shows the mean of each app's own percent. The first failure stops queued apps from
    starting; running ones finish rather than being killed mid-copy. Every app installs into one flat folder and
    ships the same native DLLs, legal files and `.notices`, so the copy phase is serialized: the apps' headless
    installers take `TrayAppDotNETInstallDirectoryLock` (a `Global\` mutex named from the install directory hash)
    around `CopyInstallPayload` (the in-app update worker takes it around `UpdateFileTransaction.Apply`), and portable installs extract to staging in parallel, then copy into the folder
    one app at a time. Uninstall removes the shared files in C# (`PrepareUninstall`, the `--uninstall-prepare`
    stage), under the same lock: it renames the app's exe to `<App>.exe.uninstalling` (a running image can be renamed,
    and the name leaves the `*.exe` pattern), deletes the shared files only when no other exe remains, and
    `DetachedCleanup` (a fixed System32 `cmd.exe` line started after the helper exits) then deletes the exe under
    either name plus the app's own files.
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
- Tests (the projects in `TrayAppDotNET.slnx`; `.github/workflows/tests.yml` runs all of them plus the Python and
  endpoint tests):
  - Common: `TrayAppDotNETCommon/tests/TrayAppDotNETCommon.Tests` (every Common runtime and UI test plus the XML
    generator tests; tests of Common types belong here, not in an app test project) and
    `TrayAppDotNETCommon/tests/AxamlPropertyLinker.Tests`
  - Shared helpers: `TrayAppDotNETCommon/tests/TrayAppDotNETCommon.TestSupport` (`AvaloniaTestHost` `Run`/`RunAsync`/
    `RunWithFluentTheme`/`RunWithFluentThemeAsync`, `ShowActivated`, `FocusForKeys`; `KeyboardInput.PressKey`;
    `TestPalettes.Default`; `TestSettingsDirectory.Redirect`/`UseFresh`; `TempDirectory`; `EnvironmentFact`;
    `TestProcesses`). A test project opts in with `<TrayAppDotNETUseTestSupport>true</TrayAppDotNETUseTestSupport>`
    (reference and global using come from `TrayAppDotNET.Parent.targets`) and redirects its settings folder from a
    one-line `[ModuleInitializer]` (`TestAssemblySetup.cs`); the installer, generator and tool tests do without it
  - Apps: `BatteryTrayAppDotNET/tests/BatteryTrayAppDotNET.Tests`, `BrightnessTrayAppDotNET/tests/BrightnessTrayAppDotNET.Tests`,
    `BrightnessTrayAppDotNET/tests/BrightnessTrayAppDotNET.DDCSimulator.Tests`,
    `FanControlTrayAppDotNET/tests/FanControlTrayAppDotNET.Tests`, `NetworkTrayAppDotNET/tests/NetworkTrayAppDotNET.Tests`,
    `TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests`, `VolumeTrayAppDotNET/tests/VolumeTrayAppDotNET.Tests`
  - Installer: `TrayAppDotNETInstaller/tests/TrayAppDotNETInstaller.Tests`
  - Tools: `tools/AppIconGenerator.Tests`
  - Python: `.github/scripts/tests/test_publish.py` (`publish.py`); endpoint updater: `npm test` in
    `.github/.cloudflare/version-endpoint-updater`
  - Manual probes outside every solution (console programs, not test runners):
    `BrightnessTrayAppDotNET/tests/NightLightTester`, `BrightnessTrayAppDotNET/tests/WindowsBrightnessProbe`,
    `VolumeTrayAppDotNET/tests/FormatProbe`; `tools/GlyphOpticalCenter` is a glyph-centering utility
  - Opt-in tests use `[EnvironmentFact]` and show as skipped unless their environment variable is set
    (for example the elevated kill-helper smoke test, which can display UAC)

## Shared Infrastructure

- Common tray shell and Win32 notification icon:
  - `TrayAppDotNETCommon/src/UI/Tray/TrayAppDotNETShellTrayIcon.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/NativeIcon.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/TrayIconRenderer.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/TrayIconRenderQueue.cs`
  - `TrayAppDotNETCommon/src/UI/Tray/TrayIconPresenter.cs`: `TrayIconPresenter<TInput>` and `ITrayIconRenderer<TInput>`;
    every app's tooltip, render dedupe (last requested input plus taskbar icon size), render queue and app-icon
    fallback
  - `TrayAppDotNETCommon/src/UI/Tray/TrayIconRasterizer.cs`: bitmap to PNG to HICON, glyph centering and typeface
    resolution for every tray renderer
  - `TrayAppDotNETCommon/src/UI/Tray/TrayMenuWindow.cs` and `TrayMenuController.cs`: the tray context menu with the
    standard Settings and Exit tail, and its warm slot and invalidation
  - `TrayAppDotNETCommon/src/UI/ContextMenus/ContextMenuWindow.cs`, `EditableContextMenuWindow.cs`,
    `MenuKeyboardSelection.cs`: the menu windows every tray and in-window context menu is built on
  - `TrayAppDotNETCommon/src/UI/Tray/TrayWindowToggle.cs`: the close-on-click guard for windows a tray click toggles
  - `TrayAppDotNETCommon/src/UI/Tray/AppIconLoader.cs`: every app's window, tray and bitmap app icon
  - `TrayAppDotNETCommon/src/UI/Tray/TrayClickModifiers.cs`, `TrayTooltipText.cs`, `TrayWheelNotchAccumulator.cs`,
    `TrayPopupPositioning.cs`, `TrayWorkArea.cs`, `TaskbarForegroundTracker.cs`
- Common settings data and theming:
  - `TrayAppDotNETCommon/src/Models/AppSettingsCommon.cs`: the base of every app's `AppSettings` (`Save()` always
    writes; `RequestSave()` and `SaveOnExit()` are implicit saves that `Autosave` gates; `SetState` persists runtime
    UI state such as dock positions without raising `Changed`), plus the shared enums
    (`TrayAppDotNETThemeMode`, `TrayAppDotNETContextMenuPosition`, `TrayAppDotNETTrayIconStyle`)
  - `TrayAppDotNETCommon/src/Serialization`: `TrayXmlFileStore<T>` (one writer per file, `ScheduleSave`/`SaveNow`/
    `Flush`), `DataFileLoader` (missing, loaded, corrupt or locked; a corrupt file is kept as a timestamped
    `.corrupt-*` copy), `AtomicFile` (write to a temporary file, then replace) and the generated-serializer runtime
  - `TrayAppDotNETCommon/src/Visuals/AppTheme.cs`: `theme.xml` load/save, `AppTheme.ResolveIsLight` (the only
    light/dark resolver)
  - `TrayAppDotNETCommon/src/UI/AppSettingsPalette.cs`: `AppSettingsPalette.Create`/`CreateForFlyout`, the only
    settings palette factory
- Common process, IPC and native services:
  - `TrayAppDotNETCommon/src/Services/ProcessExitWatch.cs`: one wait per PID shared by every subscriber, with start
    time validation (watcher, Volume sessions, helper parent watches, Task Manager's broker)
  - `TrayAppDotNETCommon/src/Services/Ipc`: `TrayAppDotNETPipeSecurity` (user-only pipe DACL and owner, verified
    client and server) and `TrayAppDotNETHelperProcess` (launching and hosting helper processes with
    `--parent-pid`/`--parent-start`/`--pipe-name`)
  - `TrayAppDotNETCommon/src/Interop/Wmi`: `WmiConnection` with bounded enumeration (Brightness WMI brightness, Task
    Manager memory metadata)
  - `TrayAppDotNETCommon/src/Interop/PropertyStore.cs` (PROPVARIANT, `IPropertyStore`, AUMID key),
    `IconExtraction.cs`, `ShellLink.cs`, `Ole32.cs` (`ComApartmentScope`), `NtDll.cs`, `Kernel32.cs`, `User32.cs`
  - `TrayAppDotNETCommon/src/Utils/ProcessCommandLine.cs`, `ProcessImagePath.cs`, `Safe.cs`
  - `TrayAppDotNETCommon/src/Services/ExplorerProcessLauncher.cs`: `TryShellExecute` through the desktop Explorer
    (Task Manager's IFEO registry lives in Task Manager, whose launches use its own `InteractiveUserLauncher`)
- Common app host (composition, no reflection), used by every app's `App.cs`:
  - `TrayAppDotNETCommon/src/UI/TrayAppDotNETAppHost.cs`: `TrayAppDotNETAppRunner.Run<TApp>` (rendering backend and
    language read in one pass), `TrayAppDotNETAppHost<TSettings,TTheme>` (`InitializeShell`, `TryRunAuxiliaryMode`,
    `BeginTrayMode`, `StartHotkeyService`, `StartCommonServices`, `ShowUninstallerWindow`, `Exit`, `Restart`, and the
    once-only shutdown core) and `TrayAppDotNETAppLifetime.Exit/Restart` for Common UI exit paths
  - `TrayAppDotNETCommon/src/Services/Install/TrayAppDotNETAppInstallServices.cs`: per-app install services, program
    options factory and `TrayAppDotNETUninstallHook` (Brightness gamma restore, Task Manager IFEO removal, Fan PawnIO
    cache). A hook can stop an uninstall before anything is removed: `CheckUninstall` runs in the starting process
    (`RunUninstall` refuses with the reason, shown by the uninstaller window and `--uninstall-headless`), and a reason
    returned by `BeforePrepare` stops the `--uninstall-prepare` helper as its failed stage. Task Manager uses both
    (`TaskManagerUninstallGuard`) so a per-user uninstall never deletes the exe the machine-wide taskmgr.exe redirect
    points at.
  - `TrayAppDotNETCommon/src/TrayAppDotNETExitRequest.cs`: graceful exit request channel and verified process kills
  - `TrayAppDotNETCommon/src/TrayAppDotNETConstants.cs`: shared root folder, publisher, help link, repository
  - `TrayAppDotNETCommon/src/UI/TrayAppDotNETAvalonia.cs`
  - App-specific `src/App.cs` (app services and UI only), `src/Program.cs`, `src/AppServices.cs`
- Common settings and controls:
  - `TrayAppDotNETCommon/src/UI/SettingsWindowCommon.cs` (shell, search, confirm overlay, palette from
    `CommonSettings`/`SettingsTheme`) and `SettingsWindowCommon.Sections.cs` (About, General, installation,
    Rendering, Appearance, context menu appearance and Hotkeys pages every app shares)
  - `TrayAppDotNETCommon/src/UI/TrayAppDotNETMemoryRelease.cs`: after a settings window closes, compacts the GC heap
    (`src/Utils/ManagedHeap.cs`, shared with the watcher) and trims the ANGLE Direct3D device's driver caches
    (`IDXGIDevice3::Trim`), which an idle app otherwise keeps
  - `TrayAppDotNETCommon/src/UI/CommonBindings.cs`
  - `TrayAppDotNETCommon/src/UI/Settings` (`TrayAppDotNETHotkeysSection.cs` is the hotkey editor)
  - Hotkeys: `TrayAppDotNETCommon/src/Models/Hotkey.cs` (`HotkeyBindingBase<TAction>`),
    `TrayAppDotNETCommon/src/Services/GlobalHotkeyService.cs`, key names and capture in
    `TrayAppDotNETCommon/src/UI/Hotkeys/HotkeyKeys.cs`
  - App settings windows: `<App>/src/UI/Settings/<App>SettingsWindow.cs` (page list) plus one `<Name>Page.cs`
    partial per page
  - `TrayAppDotNETCommon/src/UI/Controls`
  - `TrayAppDotNETCommon/src/UI/Controls/FlyoutSlider.cs`, `WheelStepAccumulator.cs` (whole steps from fine or
    notched wheels; horizontal input yields none)
  - `TrayAppDotNETCommon/src/UI/Controls/SearchableListBox.cs` (`ReplaceItems` rebuilds rows once)
  - `TrayAppDotNETCommon/src/UI/Controls/TrayAppDotNETDialogChrome.cs`: shared borderless-dialog title bar on the
    caption close button
  - `TrayAppDotNETCommon/src/UI/Controls/Curves/MonotonicCurveSampler.cs`: the monotonic curve math Brightness
    (three-point ends) and Fan (secant ends) evaluate and draw with
  - `TrayAppDotNETCommon/src/UI/Drag/VerticalReorderGesture.cs`: drag-to-reorder for vertical lists plus `ReorderMath`
- Common flyout base (every app flyout derives from it):
  - `TrayAppDotNETCommon/src/UI/FlyoutWindowCommon.cs`: `ShowAt`, sealed `Hide`, the `OnShowing`/`OnShown`/
    `OnRevealing`/`OnHiding`/`OnFlyoutClosed` hooks, `RebuildContent` through `FlyoutRebuildScheduler.cs`
    (`QueueRebuild`, `RebuildNow`, `FlushPendingRebuild`, held while `IsContentInteractionActive`), the Escape dismiss
    command, owned modal dialogs (`ShowOwnedDialogAsync`, closed on hide), `OpensWindow` auto-hide suppression and
    `ShowInstallUpdatePrompt`
  - `TrayAppDotNETCommon/src/UI/FlyoutTrayPlacement.cs` (+ `.axaml`): docked and saved positions, work-area max height,
    companion windows beside the flyout; `FlyoutChromeDragController.cs`: undocked frame drag
  - `TrayAppDotNETCommon/src/UI/Controls/ConfirmOverlay.cs`: `TrayAppDotNETConfirmOverlay`, the flyout confirm overlay
  - `TrayAppDotNETCommon/src/Utils/TaskExtensions.cs`: `ForgetLogged` for fire-and-forget UI tasks
- Control maps (see `CONTROL_MAP_PLAYBOOK.md`):
  - Schema and runtime index: `TrayAppDotNETCommon/src/UI/ControlMapping`
  - Shared templates: `TrayAppDotNETCommon/src/UI/ControlMap.axaml`
  - App maps: `<App>/src/UI/ControlMap.axaml`; every map class is `<Project>.UI.ControlMap`
  - Generator: `TrayAppDotNETCommon/generators/AxamlPropertyLinker/ControlMaps`
- AXAML resources (see `AXAML_HOT_RELOAD_PLAYBOOK.md`):
  - Generator: `TrayAppDotNETCommon/generators/AxamlPropertyLinker/AxamlPropertyLinkerGenerator.cs` types every
    `Prefix.Name` key (numbers, strings, colors, thicknesses, corner radii, transforms, glyphs, theme colors) as
    `owner.AxamlPrefix.Name`, and for each `[AxamlResourceStore]` dictionary emits its constructor, `Current`, and the
    Debug-only hot-reload store, `ReloadNow`, and reload event
  - Attribute and modes: `TrayAppDotNETCommon/src/UI/AxamlResourceStoreAttribute.cs`; each dictionary's
    `*Resources.cs` is only the attributed partial declaration
  - Debug-only reload runtime: `TrayAppDotNETCommon/src/UI/AXAMLResourceHotReloadStore.cs` (store, `AXAMLHotReload`
    notifier and synchronizer), `CommonAXAMLHotReload.cs`, `src/Visuals/GlyphCatalogHotReload.cs`,
    `src/Visuals/AppThemeHotReload.cs`
  - Unused key and glyph audit: `tools/axaml_resource_audit.py`
- Localization:
  - `TrayAppDotNETCommon/src/Localization/CommonStrings.resx` holds every string more than one app shows (page
    names, shared headers, click and menu-position labels, tooltips of the shared flyout buttons); each app's
    `<App>/src/Localization/Strings.resx` holds only its own text (taglines, descriptions, search keywords).
    `LocalizationManager` reads app resources first, so an app key with a Common name replaces the shared text:
    don't add one unless the app must differ, and then list it in
    `TrayAppDotNETCommon/tests/TrayAppDotNETCommon.Tests/CommonResourceOverrideTests.cs` with the reason.
  - Call sites read `L(nameof(CommonStrings.Key))` or `L(nameof(AppStrings.Key))`; format strings with placeholders
    get a `<comment>` describing them in files that use comments.
  - A line break inside a value is written `&#10;`, never as a literal break: Git checks resx files out with CRLF and
    the resource compiler keeps it, so the text would change with the checkout. `ResourceLineEndingTests` (Common
    tests) fails on any resource string that contains CR.
  - Wording: sentence case, "Hotkeys", "Tray icon", "Theme style", "System-wide", "Microsoft Store"; clicks as
    "Left-click", "Double-click", "Ctrl + left-click", "Ctrl + mouse wheel" (modifiers joined by " + " like hotkeys).
  - `python tools/resx_key_audit.py` lists unused keys, app keys shadowing Common ones, and keys three or more
    apps share outside Common.
- Time constants and throttling:
  - `TrayAppDotNETCommon/src/TimeConstants.cs`
  - `TrayAppDotNETCommon/src/Services/AsyncThrottler.cs`
- Install/update/startup:
  - `TrayAppDotNETCommon/src/ProgramStartup.cs`
  - Contracts shared with the .NET Framework installer as linked source (`<Compile Include … Link=…>` in
    `TrayAppDotNETInstaller.csproj`; keep them .NET Framework 4.8 compatible): `TrayAppDotNETCommon/src/Contracts/`
    (`InstallContract.cs`: command line, folder names, shared native DLL list, which `publish.py` also reads;
    `InstallProgress.cs`: the `[NN%]`/`[FAIL]` wire format), `src/Utils/DetachedCleanup.cs` (post-exit deletes
    through a fixed System32 `cmd.exe` line, paths in environment variables) and `src/Utils/ElevationPolicy.cs`
    (the one over-the-shoulder elevation policy)
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
- Night Light: `NightLightProvider` (backend choice; never blocks on the helper), `Interop/NightLight`
  (`NightLightHelperProcess.cs` client/server with `RetryableInitialization`, `NightLightRegistry` reads through
  `RegistryBlobCache`, `NightLightCloudStore` known-RVA mirror, `PDBSymbolResolver`, gamma ramp)
- Monitors: `Services/MonitorService.cs` (first enumeration on the refresh worker; UI hops skipped while draining),
  `Interop/DDCCI/CCD.cs` (`TryQueryActivePaths`, display numbers), `Utils/KnownDisplaysStore.cs`
- Tests: `BrightnessTrayAppDotNET/tests/BrightnessTrayAppDotNET.Tests`

### Fan Control

- App startup/lifetime: `FanControlTrayAppDotNET/src/App.cs`, `FanControlTrayAppDotNET/src/Program.cs`
- Flyout and fan cards: `FanControlTrayAppDotNET/src/UI/Flyout`
- Settings: `FanControlTrayAppDotNET/src/UI/Settings`
- Curve editor: `FanControlTrayAppDotNET/src/UI/Curves`
- Hardware service and models: `FanControlTrayAppDotNET/src/Services`, `FanControlTrayAppDotNET/src/Models`
  (`LHMService`: hardware walks on workers, registry/fans/settings applied on the UI thread, reset and re-apply on
  resume; `FanControlDirtyShutdownMarker` in the shared root, legacy path honoured)
- LibreHardwareMonitor source submodule:
  - `FanControlTrayAppDotNET/vendor/LibreHardwareMonitor/LibreHardwareMonitorLib` (the other vendored submodules,
    NCalc, HIDSharp and DiskInfoToolkit, are beside it under `vendor/`)
  - AMD CPU CCD work centers on `LibreHardwareMonitorLib/Hardware/Cpu/Amd17Cpu.cs`
- Tests: `FanControlTrayAppDotNET/tests/FanControlTrayAppDotNET.Tests`

### Network

- App startup/lifetime: `NetworkTrayAppDotNET/src/App.cs`, `NetworkTrayAppDotNET/src/Program.cs`
- Network monitor: `NetworkTrayAppDotNET/src/Services/NetworkMonitor.cs`
- Settings pages: `NetworkTrayAppDotNET/src/UI/Settings`
- Network may intentionally differ from other apps for rendering/backend behavior.
- Tests: `NetworkTrayAppDotNET/tests/NetworkTrayAppDotNET.Tests` (settings round trip and theme defaults only)

### Battery

- App startup/lifetime: `BatteryTrayAppDotNET/src/App.cs`, `BatteryTrayAppDotNET/src/Program.cs`
- Battery monitor: `BatteryTrayAppDotNET/src/Services/BatteryMonitorService.cs`
- Power plans and Energy Saver: `Services/WindowsPowerModeBackend.cs` over `Interop/PowrProf.cs` (GUID-based,
  language independent)
- Flyout: `BatteryTrayAppDotNET/src/UI/Flyout`; settings pages: `BatteryTrayAppDotNET/src/UI/Settings`
- Tests: `BatteryTrayAppDotNET/tests/BatteryTrayAppDotNET.Tests` cover the trigger engine, power-mode service and
  backend (classifier plus read-only machine checks) and the time estimator; the battery monitor, flyout and settings
  pages have no tests yet.

### Task Manager

- App startup/lifetime: `TaskManagerTrayAppDotNET/src/App.cs`, `TaskManagerTrayAppDotNET/src/Program.cs`
- Shared settings-shell derivative: `TaskManagerTrayAppDotNET/src/UI/TaskManagerWindow.cs`; settings window pages:
  `TaskManagerTrayAppDotNET/src/UI/Settings`
- Page folders: `TaskManagerTrayAppDotNET/src/UI/ProcessesPage` and `TaskManagerTrayAppDotNET/src/UI/Performance`
  (namespaces follow the folders); the Services, Users, Startup apps and App history table pages stay in `src/UI`
- Grid shared by every table (base control, row geometry, shortcut registry): `src/UI/TaskManagerGrid*.cs`
- Painted Processes table: `TaskManagerTrayAppDotNET/src/UI/ProcessesPage/ProcessTableCanvas.cs`
- Render-thread cursor-sampled row hover: `TaskManagerTrayAppDotNET/src/UI/ProcessesPage/ProcessRowHoverVisual.cs`
- Fixed-buffer process sampler: `TaskManagerTrayAppDotNET/src/Services/ProcessSnapshotService.cs` (also publishes
  each tick's process/thread/handle totals as `IRecentProcessTotals` for the Performance sampler)
- Processes Status column, mirrored from Taskmgr.exe: `TaskManagerTrayAppDotNET/src/Models/ProcessStatus.cs`,
  `TaskManagerTrayAppDotNET/src/Services/ProcessHungWindowCollector.cs`; rules in
  `documentation/docs/task_manager_process_status.md`
- Always run as administrator: `TaskManagerTrayAppDotNET/src/Services/ElevatedStartTask.cs` (per-account
  highest-privileges scheduled task, hand-off, task-start argument check) over the generated Task Scheduler COM client
  in `src/Interop/TaskSchedulerInterfaces.cs` and `TaskSchedulerSession.cs`; the elevated relaunch both machine-level
  toggles share is `src/Services/ElevatedSettingChange.cs`
- Run new task, page shortcuts and Restart explorer's fresh shell:
  `TaskManagerTrayAppDotNET/src/Services/InteractiveUserLauncher.cs` (Windows Task Manager's
  `\Microsoft\Windows\Task Manager\Interactive` task through wdc.dll `WdcRunTaskAsInteractiveUser`; no Explorer
  dependency); the restart itself is `CriticalProcessActions.RestartExplorer`
- Sidebar zoom/stretch indicator: `TaskManagerTrayAppDotNET/src/UI/TaskManagerGridMetricCard.cs`, fed by
  `TaskManagerPageLayout.GridMetricChanged` from the table pages' zoom/stretch handlers and placed above the Update
  and Settings rows through `SettingsWindowCommon.CreateSidebarFooterStatus`
- Tests: `TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests`

## Build And Packaging Files

- Shared MSBuild (the app folders have no Directory.Build.props of their own):
  - `Directory.Build.props`: imports Parent.props; x64 platforms and the configurations Debug, Prerelease, Release
    and ReleaseWithDebugging (Native AOT with symbols, no stamping)
  - `TrayAppDotNET.Parent.props`: project roles (`TrayAppDotNETIsAppProject` for `<App>/src/<App>.csproj`, found
    through the app folder's `buildnumber.txt`; `TrayAppDotNETIsTestProject` for `*.Tests`;
    `TrayAppDotNETIsCommonProject`; vendored projects get none), shared package versions, app and test project
    defaults, the app's obj\ redirect
  - `TrayAppDotNET.Parent.targets`: app references (Common, generators, Avalonia, HotAvalonia), the app `Strings.resx`
    API, test packages and the app reference of app-folder tests, `EmbedBuildInfo`, the GC heap limit (an app sets
    `TrayAppDotNETGCHeapHardLimitBytes` to raise it), legal files, `PublishAfterBuild` with its prune lists (read by
    `publish.py`), installer stamping, the generated app catalog, and the build-output unlock
    (`tools/unlock_trayappdotnet_build_outputs.ps1`; a warning names a missing script)
  - App csproj files keep only what is particular to the app (usings, extra packages and resources, Fan's vendored
    references, Task Manager's native kill helper)
- SDK pin: `global.json` (.NET 10 SDK, `rollForward: latestFeature`); workflows install it with `actions/setup-dotnet`
- App catalog, the one list of apps: `TrayAppDotNET.Apps.props` (MSBuild items). Building Common rewrites the
  generated `apps.json` at the repo root, which `publish.py`, the workflows, the version endpoint updater and
  `tools/tadn_portable_build_script.ps1` read. Adding an app: add the item, build Common, add a `<slug>-debug.yml`
  caller.
- GitHub workflows:
  - `.github/workflows/tests.yml`: every test project of `TrayAppDotNET.slnx`, `.github/scripts/tests` and the
    endpoint updater tests (push to main, pull requests, and the first job of `publish.yml`)
  - `.github/workflows/<slug>-debug.yml`: per-app triggers and path filters calling `_app-build.yml`
  - `.github/workflows/publish.yml`: tests, plan (`release_matrix` from `publish.py`), one `app-release.yml` run per
    app, then the draft release
  - `.github/workflows/trigger-version-endpoint.yml`: asks the Cloudflare updater to republish `versions.xml`; its
    manual `force` input allows a rollback, and a refusal (HTTP 409) is not retried
  - Self-hosted runners: `.github/bootstrap-{windows,debian}-runner-fleet.py` register runners labelled
    `trayapp,windows` / `trayapp,debian` named `<hostname>-trayapp-NN`; `.github/scripts/remove-linux-runners.py`
    removes the Debian fleet. Every job uses hosted runners; switch a job's `runs-on` to
    `[self-hosted, trayapp, windows]` to use the fleet.
- Release packaging script (app zips, the generated `install.bat`/`install-all.bat`, then stamping per-app
  `Installer_<App>_<version>.exe` and the suite `Installer_TrayAppDotNET_<version>.exe` from one factory build, then
  `--verify-installer` on each stamped image; `versions.xml` lists the released apps under `<applications>`):
  `.github/scripts/publish.py`
- App icons and the installer icon are generated by `tools/AppIconGenerator` (target `TADN` writes `TrayAppDotNETInstaller/app.ico`)
- Release and ReleaseWithDebugging builds of an app publish Native AOT (`TrayAppDotNET.Parent.props`):
  - `SelfContained=true`
  - `PublishAot=true`
  - `PublishSingleFile=false` (the publish folder holds the exe plus the native Skia, ANGLE and HarfBuzz DLLs)

## Launch Model

- Running an app with no arguments starts normal mode.
- Normal mode uses the crash watcher process, then the watcher starts the monitored app process.
- The Run on startup shortcut passes `--autostart`; the watcher forwards it to the monitored process, where
  `TrayAppDotNETProgram.IsStartupLaunch` exposes it. Task Manager's Start minimized option only applies then.
- The installer passes `--hidden` to the apps it launches; `TrayAppDotNETProgram.IsHiddenLaunch` exposes it and
  Task Manager then starts in the tray without showing its window. The watcher rebuilds the monitored command
  line, so only the flags in `TrayAppDotNETProgram.ForwardedLaunchArguments` survive it.
- `--settings-dir <path>` replaces the per-app folder: `TrayAppDotNETProgram.Run` resolves it to an absolute path
  (`SettingsDirectoryOverride`) before the watcher hop, and `AppLocalAppDataDirectory` returns it, so every
  `Program.AppLocalAppDataDirectory` consumer (settings, theme, logs, data stores) follows. The watcher, the
  rendering restart and Restart from installed (`SettingsDirectoryCommandLine`), the update restarter and an
  explicitly enabled startup shortcut carry it forward. Unelevated helper processes started outside `Run`
  (Brightness Night Light and DDC helpers) get it through `TrayAppDotNETProgram.ConfigureHelperProcess` or
  `HelperProcessArguments`, apply it with `InitializeHelperProcess`, and log to `<helper>.log` in the app's log
  folder. Elevated helpers (Task Manager's broker, `--set-taskmgr-replacement` and `--set-elevated-start`) get no
  override and use `TADNLog.InitializeForElevatedProcess`, which writes no file (the broker forwards lines to the
  app's log). The shared `LocalAppDataRoot` (versions manifest cache, install folder, download caches) does not move.
- Task Manager's Always run as administrator task (`ElevatedStartTask`) starts the system install elevated with
  `--elevated-start-task [--autostart] [--hidden]`; `Program.Main` checks for it before every other mode and refuses
  any other argument. An unelevated plain start (no arguments, `--autostart`, `--hidden`, or a redirected taskmgr.exe
  start) runs that task before the watcher hop and exits; `--settings-dir` starts never hand off.
- Single instance: a second launch's watcher asks the running app to exit through
  `Local\TrayAppDotNET-ExitRequest-<pid>` (`TrayAppDotNETExitRequest`); the app runs its normal exit (exit code 0,
  so its watcher stops too). Only when the single-instance mutex is still held after
  `TimeConstants.GracefulExitRequestTimeoutMs` are the old processes killed, each one first proven by image name and
  start time. Uninstall preparation and the update installer stop running copies the same way.
- Exit paths: tray Exit, watcher death, exit requests, update install, the rendering restart, Restart from installed
  and a Windows session end all run the host's shutdown core once (hooked to both `ShutdownRequested` and `Exit`).
  A session end exits with `CrashHandler.SessionEndingExitCode` (3). The watcher honors that code only when it sees
  Windows reporting the session as shutting down as the app exits (the C runtime's abort also exits with 3); it then
  waits and relaunches once the session continues. Every relaunch counts toward the rapid-restart cap.
- Installer (`--installer`) and uninstaller (`--uninstall`) modes only read settings for styling: no settings write,
  no startup shortcut, no app services.
- Useful arguments are documented in repo `README.md` and `documentation/docs/getting-started/launch-arguments.md`:
  - `--install-headless local|system` (`--install` is the older alias)
  - `--installlocal`
  - `--installsystem`
  - `--uninstall <installDir> --scope <scope>`
  - `--uninstall-headless local|system`
  - `--settings-dir <path>`
  - `--watcher`
  - `--monitored --watcher-pid <pid>`

## Repeated Discovery To Avoid

- Do not rediscover the same topology with full-repo scans if the task clearly maps to the sections above.
- Start from the named app plus `TrayAppDotNETCommon`.
- For build/release tasks, inspect `.csproj`, `.props`, `.targets`, workflows, and publish scripts early.
