# TrayAppDotNETCommon

The shared library every TrayAppDotNET app references. An app keeps only what is particular to it (its services,
flyout, tray icon renderer, settings pages and models); everything the apps have in common lives here. It targets
.NET 10 and must stay Native AOT and trim safe: no reflection-based serialization, no `ComImport`, no
`Marshal.GetDelegateForFunctionPointer`.

## What Common owns

- **Startup and lifetime** (`src/ProgramStartup.cs`, `src/CrashHandler.cs`, `src/TADNLog.cs`,
  `src/SingleInstanceCoordinator.cs`, `src/TrayAppDotNETExitRequest.cs`): the command line, the crash watcher process,
  single instance with a graceful exit request, logging (including helper-process logs) and crash reports.
- **App host** (`src/UI/TrayAppDotNETAppHost.cs`): `TrayAppDotNETAppRunner.Run<TApp>` and
  `TrayAppDotNETAppHost<TSettings, TTheme>`, which start the tray mode, hotkeys and common services and run the one
  shutdown path every exit takes.
- **Install, update and uninstall** (`src/Services/Install`, `src/Services/UpdateCheckService.cs`): local and system
  installs, shortcuts, run on startup, hash-verified in-app updates applied as a journaled transaction, and uninstall
  with per-app hooks.
- **Settings and data files** (`src/Models/AppSettingsCommon.cs`, `src/Serialization`): the settings base class, the
  generated XML serializers' runtime, atomic writes, corrupt-file backups and one writer per file.
- **UI**: the tray icon and tray menu (`src/UI/Tray`, `src/UI/ContextMenus`), the flyout base and docking
  (`src/UI/FlyoutWindowCommon.cs` and friends), the settings window shell and its shared pages
  (`src/UI/SettingsWindowCommon*.cs`, `src/UI/Settings`), controls and dialogs (`src/UI/Controls`), hotkeys
  (`src/UI/Hotkeys`, `src/Services/GlobalHotkeyService.cs`), control maps (`src/UI/ControlMapping`,
  `src/UI/ControlMap.axaml`), themes, glyphs and fonts (`src/Visuals`).
- **Native and process services** (`src/Interop`, `src/Services/ProcessExitWatch.cs`, `src/Services/Ipc`): Win32 and
  COM declarations, WMI, the property store, icon extraction, shell links, process exit watches and the secured pipes
  the helper processes use.
- **Localization** (`src/Localization/CommonStrings.resx`): every string more than one app shows.

`.agents/PROJECT_MAP.md` lists the individual files.

## Source generators

`generators/` holds two Roslyn source generators. They are not referenced as libraries: `TrayAppDotNET.Parent.targets`
adds them to Common and to every app project as analyzers (`OutputItemType="Analyzer"`,
`ReferenceOutputAssembly="false"`), together with every `*.axaml` file as `AdditionalFiles`.

- `XmlSourceGenerator`: reflection-free XML readers and writers for the settings, theme and data models.
- `AxamlPropertyLinker`: typed accessors for AXAML resource keys, the control-map classes generated from
  `ControlMap.axaml`, and the Debug provenance data.

## The installer boundary

`TrayAppDotNETInstaller` (the suite installer factory) is a .NET Framework 4.8 WPF app and does not reference Common.
It compiles a few Common files as linked source instead (`<Compile Include … Link=…>` in its csproj):
`src/Contracts/*.cs` (the install command line, folder names, shared native DLL list and progress wire format),
`src/Utils/DetachedCleanup.cs` and `src/Utils/ElevationPolicy.cs`. Keep those files .NET Framework 4.8 compatible.
The generated `TrayAppDotNETCommon.Contracts.TrayAppDotNETAppCatalog` (the app list from `TrayAppDotNET.Apps.props`)
is emitted into both projects.

## Tests

- `tests/TrayAppDotNETCommon.Tests`: every Common runtime and UI test (Avalonia headless), along with the XML
  generator tests.
- `tests/TrayAppDotNETCommon.TestSupport`: helpers every .NET test project shares (headless host, palettes, key
  input, settings folder redirect, temporary folders, opt-in facts).
- `tests/AxamlPropertyLinker.Tests`: the AXAML generator tests.

`TrayAppDotNETCommon.slnx` opens Common with its generators and tests; the root `TrayAppDotNET.slnx` builds
everything. Build and test commands are in the [root README](../README.md#development).
