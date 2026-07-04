# Native AOT Warning Audit

Date: 2026-07-04

Workspace: `C:\Users\Alchemy\Desktop\@Workbench\@TRAY_APP_DOT_NET\TrayAppDotNET`

Scope:

- `BatteryTrayAppDotNET/src/BatteryTrayAppDotNET.csproj`
- `BrightnessTrayAppDotNET/src/BrightnessTrayAppDotNET.csproj`
- `FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj`
- `NetworkTrayAppDotNET/src/NetworkTrayAppDotNET.csproj`
- `VolumeTrayAppDotNET/src/VolumeTrayAppDotNET.csproj`

Audit logs are under:

```text
.artifacts/native-aot-warning-audit-20260704/logs/
```

## Method

The normal audit pass used Native AOT publish with warnings collected instead
of failing immediately:

```powershell
dotnet publish <app>.csproj `
  --configuration Release `
  --runtime win-x64 `
  --output .artifacts/native-aot-warning-audit-20260704/publish/<app> `
  --nologo `
  --verbosity normal `
  -p:Platform=x64 `
  -p:PublishAot=true `
  -p:IlcTreatWarningsAsErrors=false `
  -p:SkipKillRunningInstance=true `
  -p:SkipPublishAfterBuild=true `
  -p:TrayAppDotNETAggregateBuild=false `
  -p:TrimmerSingleWarn=false
```

Additional unmasked passes were run for projects that currently hide Native AOT
warnings through `NoWarn`:

```powershell
-p:NoWarn=
```

`FanControlTrayAppDotNET` also needed a workaround pass:

1. `dotnet restore` with `PublishAot=false`
2. `dotnet publish --no-restore` with `PublishAot=true`

That mirrors the release-script approach in `.github/scripts/publish.py`.

Note: `TrayAppDotNET.Parent.props` sets `IlcTreatWarningsAsErrors=true` when a
runtime identifier is present. The audit intentionally passed
`IlcTreatWarningsAsErrors=false` to enumerate warnings instead of stopping at
the first ILC warning/error.

## Result summary

| App | Direct Native AOT publish | Normal warning summary | Unmasked warning summary | Notes |
| --- | --- | ---: | ---: | --- |
| BatteryTrayAppDotNET | Passed | 6 warnings | Not needed | Common AXAML `IL2026` warnings only |
| BrightnessTrayAppDotNET | Passed | 6 warnings | Not needed | Common AXAML `IL2026` warnings only |
| FanControlTrayAppDotNET | Failed during restore | Direct publish blocked | 59 warnings after restore workaround and `NoWarn=` | Direct path fails with `NETSDK1207`; broad project `NoWarn` hides the real warning set |
| NetworkTrayAppDotNET | Passed | 2 warnings | Not needed | Common AXAML `IL2026` warnings only |
| VolumeTrayAppDotNET | Passed | 2 warnings | 7 warnings with `NoWarn=` | Common AXAML plus hidden TraceEvent `IL2067`/`IL3050` |

## Post-`NoWarn` removal build

The Native AOT warning suppressions were removed from these files and the
publish audit was rerun:

- `VolumeTrayAppDotNET/src/VolumeTrayAppDotNET.csproj`
- `FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj`
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj`

New logs:

```text
.artifacts/native-aot-warning-audit-20260704-after-nowarn-removal/logs/
```

Direct publish results:

| App | Result | MSBuild warning summary | Warning IDs / error |
| --- | --- | ---: | --- |
| BatteryTrayAppDotNET | Passed | 6 warnings | `IL2026` |
| BrightnessTrayAppDotNET | Passed | 3 warnings | `IL2026` |
| FanControlTrayAppDotNET | Failed during restore | 0 warnings | `NETSDK1207` on LibreHardwareMonitor `TargetFramework=net472` |
| NetworkTrayAppDotNET | Passed | 2 warnings | `IL2026` |
| VolumeTrayAppDotNET | Passed | 4 warnings | `IL2026`, `IL2067`, `IL3050` |

FanControl workaround publish results:

| Pass | Result | MSBuild warning summary | Warning IDs |
| --- | --- | ---: | --- |
| Restore with `PublishAot=false`, then `publish --no-restore` with `PublishAot=true` | Passed | 69 warnings | `IL2026`, `IL2055`, `IL2067`, `IL2072`, `IL2075`, `IL2077`, `IL2090`, `IL2091`, `IL3050`, `IL3052` |

FanControl still also emits these ILC diagnostics:

```text
System.Management.WbemObjectTextSrc..ctor() will always throw
System.Management.WbemStatusCodeText..ctor() will always throw
System.Management.WbemContext..ctor() will always throw
```

## Post-compiled-AXAML resource dictionary build

The runtime AXAML dictionary URI loads were replaced with typed compiled
resource dictionaries. This keeps the AXAML files as the source of truth while
moving app code away from `AvaloniaXamlLoader.Load(Uri)`, which is the Native
AOT-unsafe API.

New logs:

```text
.artifacts/native-aot-warning-audit-20260704-compiled-axaml/logs/
```

Resource dictionaries converted to `x:Class` dictionaries:

- `TrayAppDotNETCommon/src/UI/SettingsWindowCommon.axaml`
- `TrayAppDotNETCommon/src/UI/CommonBindings.axaml`
- `TrayAppDotNETCommon/src/UI/FlyoutUndockButtonController.axaml`
- `TrayAppDotNETCommon/src/UI/Settings/TrayAppDotNETAboutPage.axaml`
- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutCell.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/Cards.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/ColorPickerWindow.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/DialogChrome.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/FlyoutCards.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/FlyoutSlider.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/SearchableListBox.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/SettingsUI.axaml`
- `TrayAppDotNETCommon/src/UI/Controls/UpdateConfirmationWindow.axaml`

Verification:

- `dotnet build TrayAppDotNET.slnx -c Debug -p:SkipKillRunningInstance=true -p:SkipPublishAfterBuild=true -m:1`
  passed with 0 warnings.
- `dotnet test TrayAppDotNET.slnx -c Debug --no-build -m:1` passed:
  76 tests.
- Source search found no remaining `AvaloniaXamlLoader.Load(Uri)` call and no
  AXAML `UnconditionalSuppressMessage` in app/common source.

Native AOT publish results after the AXAML fix:

| App | Result | MSBuild warning summary | Remaining warning IDs / error |
| --- | --- | ---: | --- |
| BatteryTrayAppDotNET | Passed | 0 warnings | None |
| BrightnessTrayAppDotNET | Passed | 0 warnings | None |
| FanControlTrayAppDotNET | Failed during restore | 0 warnings | `NETSDK1207` on LibreHardwareMonitor `TargetFramework=net472` |
| NetworkTrayAppDotNET | Passed | 0 warnings | None |
| VolumeTrayAppDotNET | Passed | 4 warning lines, 2 unique warnings | `IL2067`, `IL3050` from TraceEvent |

FanControl workaround publish after the AXAML fix:

| Pass | Result | MSBuild warning summary | Warning IDs |
| --- | --- | ---: | --- |
| Restore with `PublishAot=false`, then `publish --no-restore` with `PublishAot=true` | Passed | 104 warning lines, 41 unique warnings | `IL2026`, `IL2055`, `IL2067`, `IL2072`, `IL2075`, `IL2077`, `IL2090`, `IL2091`, `IL3050`, `IL3052` |

No remaining FanControl workaround warning references
`AvaloniaXamlLoader`, `TrayAppDotNETAXAMLResources`, `FanFlyoutCell`,
`TrayAppDotNETAboutPage`, `FlyoutUndock`, or `ControlAXAMLResources`.

## Targeted build-blocker pass

New logs:

```text
.artifacts/native-aot-warning-audit-20260704-targeted-fixes/logs/
```

Changes verified:

| Item | Result | Notes |
| --- | --- | --- |
| Volume TraceEvent | Suppressed | Added a narrow RuntimeIdentifier-only `NoWarn` in `VolumeTrayAppDotNET.csproj`; default Volume Native AOT publish passed with 0 warnings and 0 errors. |
| FanControl LibreHardwareMonitor `OpCode.Open()` | Fixed | Removed the local `RequiresUnreferencedCode` annotation from `OpCode.Open()`. The method uses unmanaged executable buffers plus typed delegate marshaling, not trim-sensitive managed reflection. The `Computer.Open()` warning no longer appears. |
| FanControl DiskInfoToolkit | Not fixed by trim descriptor | A `TrimmerRootDescriptor` preserving `DiskInfoToolkit` did not clear `IL2091`. The warning is dataflow annotation debt in generic `PtrToStructure<T>` helpers, not missing rooted metadata. The ineffective descriptor was removed. |
| FanControl HidSharp | Not fixed by trim descriptor | A `TrimmerRootDescriptor` preserving `HidSharp` did not clear `IL2075`. `TrimMode=partial` also kept the warning and exposed additional package warnings. The ineffective descriptor was removed. |

Default publish status after the targeted pass:

| App | Result | Warning/error set |
| --- | --- | --- |
| VolumeTrayAppDotNET | Passed | 0 warnings, 0 errors |
| FanControlTrayAppDotNET | Failed | `DiskInfoToolkit` `IL2104`, `HidSharp` `IL2104`, `Parlot` `IL2104`/`IL3053`, `System.Management` `IL2104`/`IL3053` |

FanControl workaround publish with warnings not treated as errors confirms that
the remaining DiskInfoToolkit details are `IL2091` and the remaining HidSharp
details are `IL2075`.

## Post-System.Management / WMI closure fix

New logs:

```text
.artifacts/native-aot-warning-audit-20260704-ipmi-wmi-fix/logs/
```

Changes verified:

| Item | Result | Notes |
| --- | --- | --- |
| IPMI/WMI | Removed from Release Native AOT closure | LibreHardwareMonitor now defaults `DisableIpmiWmi=true` for `net10.0` `Release` / `ReleaseWithDebugging` builds and removes `Hardware/Motherboard/Lpc/Ipmi.cs` plus the direct `System.Management` package reference. |
| RAM SPD / WMI transitive dependency | Removed from Release Native AOT closure | LibreHardwareMonitor now defaults `DisableRamSpd=true` for `net10.0` `Release` / `ReleaseWithDebugging` builds, excludes RAMSPDToolkit files/package reference, and keeps `MemoryGroup` limited to virtual/total memory sensors when RAM SPD probing is disabled. |
| Debug behavior | Preserved | `dotnet build FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj -c Debug -p:SkipKillRunningInstance=true -p:SkipPublishAfterBuild=true -m:1` passed; Debug LibreHardwareMonitor still includes `System.Management` / RAMSPDToolkit paths. |
| Release Native AOT workaround publish | Passed | Restore with `PublishAot=false`, then `publish --no-restore` with `PublishAot=true` passed with warnings not treated as errors. No `System.Management`, `Wbem`, `RAMSPDToolkit`, or `Microsoft_IPMI` matches remain in the publish log or Release `LibreHardwareMonitorLib.dll`. |

Remaining FanControl warning IDs after this fix:

| ID | Count | Source |
| --- | ---: | --- |
| `IL2091` | 2 | DiskInfoToolkit generic `PtrToStructure<T>` helpers |
| `IL2075` | 3 | HidSharp Linux `uname` reflection plus Parlot type reflection |
| `IL2026` | 28 | Parlot / FastExpressionCompiler expression compilation |
| `IL2055` | 1 | Parlot `MakeGenericType` |
| `IL2072` | 1 | Parlot `Expression.New(Type)` dataflow |
| `IL2090` | 1 | Parlot generic number parser reflection |
| `IL3050` | 10 | Parlot/FastExpressionCompiler dynamic code in the current publish log; a clean LibreHardwareMonitor rebuild can also surface its enum/marshal analyzer warnings |

## Post-NCalc source / Parlot compile-path closure

New logs:

```text
.artifacts/native-aot-warning-audit-20260704-ncalc-source/logs/
```

Changes verified:

| Item | Result | Notes |
| --- | --- | --- |
| NCalc source reference | Kept | FanControl now builds `NCalc.Core`, `NCalc.Parser`, `NCalc.Domain`, and `NCalc.SourceGenerators` from the local `FanControlTrayAppDotNET/NCalc` source tree. |
| Optional Parlot parser compilation | Removed from Release Native AOT | `NCalc.Parser` excludes the `NCalc.EnableParlotParserCompilation` branch under `DISABLE_PARLOT_PARSER_COMPILATION`; FanControl passes that define for Release RID builds. |
| Parlot dynamic compile bodies | Removed from Native AOT analysis | FanControl passes `NativeAotSubstitutions.xml` to ILC and removes `Parlot.Fluent.Parser<T>.Compile`, `Build`, and `BuildAsNonCompilableParser`. Those methods are not used in Native AOT because dynamic code is unsupported and NCalc's explicit compile switch is disabled. |
| Debug behavior | Preserved | `dotnet build FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj -c Debug -p:SkipKillRunningInstance=true -p:SkipPublishAfterBuild=true -m:1` passed with 0 warnings and 0 errors. |
| Release Native AOT workaround publish | Passed | Restore with `PublishAot=false`, then `publish --no-restore` with `PublishAot=true` passed with warnings not treated as errors. No `Parlot`, `FastExpressionCompiler`, or NCalc trim/AOT warning lines remain in the publish log. |
| Default Release Native AOT publish | Failed on remaining packages only | With `IlcTreatWarningsAsErrors=true`, default publish now reports only `DiskInfoToolkit` `IL2104` and `HidSharp` `IL2104`; no NCalc/Parlot assembly warning remains. |

Remaining FanControl warning IDs after this fix:

| ID | Count | Source |
| --- | ---: | --- |
| `IL2091` | 2 | DiskInfoToolkit generic `PtrToStructure<T>` helpers |
| `IL2075` | 1 | HidSharp Linux `uname` reflection |

## Build blocker: FanControl direct publish fails before warnings

Direct FanControl Native AOT publish failed before warning collection:

```text
NETSDK1207: Ahead-of-time compilation is not supported for the target framework.
...LibreHardwareMonitorLib.csproj::TargetFramework=net472
```

Evidence:

- Log: `.artifacts/native-aot-warning-audit-20260704/logs/FanControlTrayAppDotNET.log`
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj:3`
  targets `net472;netstandard2.0;net8.0;net9.0;net10.0`.
- `build.ps1:618` uses a single `dotnet publish` path for Native AOT.
- `.github/scripts/publish.py:432` already avoids passing `PublishAot` during
  FanControl restore.

Cause:

Native AOT publish is leaking into the LibreHardwareMonitor restore graph, so
the SDK evaluates the `net472` target with AOT enabled and aborts. The
`ProjectReference` metadata in `FanControlTrayAppDotNET.csproj:77` attempts to
force `TargetFramework=net10.0`, but the direct restore graph still evaluates
`net472`.

Proposed fixes:

1. Update `build.ps1` to special-case FanControl Native AOT like
   `.github/scripts/publish.py`: restore with `PublishAot=false`, then publish
   with `--no-restore -p:PublishAot=true`.
2. Strengthen the LibreHardwareMonitor project reference in
   `FanControlTrayAppDotNET.csproj:77` by also removing
   `RuntimeIdentifier;RuntimeIdentifiers` from propagated global properties.
   Verify whether direct publish then stops evaluating `net472`.
3. If project-reference metadata still does not affect restore, add a narrow
   app-build property that constrains LibreHardwareMonitor to `net10.0` only
   for the FanControl Native AOT path. Keep this local to the app integration
   because LibreHardwareMonitor is vendored/external code.

## Warning group 1: Common AXAML resource loaders

Warnings:

- `IL2026`
- Trigger: `Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(Uri, Uri)` /
  `AvaloniaXamlLoader.Load(Uri)` has `RequiresUnreferencedCode`.

Original locations before the compiled-AXAML fix:

- `TrayAppDotNETCommon/src/UI/TrayAppDotNETAXAMLResources.cs:14`
- `TrayAppDotNETCommon/src/UI/FlyoutUndockButtonController.cs:260`
- `TrayAppDotNETCommon/src/UI/Settings/TrayAppDotNETAboutPage.cs:477`

Affected apps before the compiled-AXAML fix:

- Battery: all three locations
- Brightness: all three locations
- Network: `TrayAppDotNETAXAMLResources` and `TrayAppDotNETAboutPage`
- Volume: `TrayAppDotNETAXAMLResources` and `TrayAppDotNETAboutPage`
- FanControl: common compile warnings, plus app-specific warnings when
  suppressions are removed

Cause:

The resources are static embedded AXAML dictionaries, but the trimmer cannot
prove that a string/URI load will only reference preserved resource assemblies.
The original proposed workaround was a narrow source-level suppression around a
known dictionary helper. That is no longer the chosen fix.

Implemented fix:

1. Add `x:Class` to each affected dictionary and add a small
   `ResourceDictionary` code-behind class that calls
   `AvaloniaXamlLoader.Load(this)`.
2. Replace each call-site URI load with construction of the typed compiled
   resource dictionary.
3. Convert `ControlAXAMLResources` to the same known-dictionary typed path and
   remove its `UnconditionalSuppressMessage`.
4. Verify Native AOT publish again. Battery, Brightness, and Network now have
   0 warnings; Volume only has TraceEvent warnings; FanControl workaround has
   no AXAML warnings.

## Warning group 2: FanControl AXAML loader

Warnings:

- `IL2026`

Original location before the compiled-AXAML fix:

- `FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutCell.cs:194`

Evidence:

- Hidden by `FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj:42`
  in the normal build.
- Visible in
  `.artifacts/native-aot-warning-audit-20260704/logs/FanControlTrayAppDotNET.unmasked-nowarn-no-restore.log`.

Implemented fix:

Resolved by converting `FanFlyoutCell.axaml` to
`FanControlTrayAppDotNET.UI.FanFlyoutCellResources` and constructing that typed
compiled dictionary from `FanFlyoutCell.LoadResources()`. The post-fix
FanControl workaround publish has no AXAML warning for this file.

## Warning group 3: Volume TraceEvent ETW dependency

Warnings:

- `IL3050`: `Microsoft.Diagnostics.Tracing.TraceEvent.TraceEvent(...)` uses
  `System.Dynamic.DynamicObject`.
- `IL2067`: `DynamicTraceEventData.GetDefaultValueByType(Type)` uses
  `Activator.CreateInstance(Type)` without the required annotations.

Locations and dependencies:

- App usage: `VolumeTrayAppDotNET/src/Audio/BluetoothCodecMonitor.cs:63`
- Package reference: `VolumeTrayAppDotNET/src/VolumeTrayAppDotNET.csproj:74`
- Current suppression:
  `VolumeTrayAppDotNET/src/VolumeTrayAppDotNET.csproj:39`

Cause:

`BluetoothCodecMonitor` uses TraceEvent to consume the
`Microsoft.Windows.Bluetooth.BthA2dp` ETW provider. TraceEvent carries dynamic
payload helpers that are not Native AOT friendly even though this app only
needs a narrow realtime provider path.

Proposed fixes:

1. Best fix: replace TraceEvent in `BluetoothCodecMonitor` with a narrow
   native ETW/TDH implementation for the BthA2dp provider and the exact fields
   consumed by the app. This removes the dynamic TraceEvent dependency and the
   publish cleanup for TraceEvent sidecar DLLs.
2. Medium fix: move Bluetooth codec ETW monitoring into a small non-AOT helper
   process and keep the tray app Native AOT. Use a simple IPC payload for codec
   updates.
3. Short-term containment: use a narrow tracked suppression only if runtime
   smoke tests confirm Bluetooth codec detection works in the Native AOT app.
   Do not restore a broad project-wide `NoWarn` list.
4. Brightness references `Microsoft.Diagnostics.Tracing.TraceEvent` but the
   audit did not find reachable TraceEvent warnings. If unused, remove the
   package reference from `BrightnessTrayAppDotNET.csproj`.

Targeted update:

The short-term containment path has been applied in the RuntimeIdentifier-only
property group. Default Volume Native AOT publish now passes with 0 warnings.

## Warning group 4: FanControl NCalc, Parlot, and FastExpressionCompiler

Warnings:

- `IL2026`
- `IL2055`
- `IL2072`
- `IL2075`
- `IL2090`
- `IL3050`

App usage:

- `FanControlTrayAppDotNET/src/Models/DataSource.cs:121`
- `FanControlTrayAppDotNET/src/UI/ProbeValueFormatter.cs:62`

Representative dependency warnings:

- `Parlot.Fluent.*.Compile(...)`
- `Parlot.Compilation.ExpressionHelper.*`
- `FastExpressionCompiler.ExpressionCompiler.*`
- `System.Linq.Expressions.Expression.Field/Property(...)`
- `System.Reflection.Emit.DynamicMethod`
- `System.Type.MakeGenericType(...)`

Cause:

FanControl uses NCalc expressions for user transforms. NCalc pulls in Parlot and
FastExpressionCompiler paths that build expression trees and dynamic methods.
Those paths are hostile to Native AOT. The former project-wide `NoWarn` hid
them; after suppression removal they remain visible in FanControl workaround
publishes.

Proposed fixes:

1. Replace NCalc usage with a small local AOT-safe expression evaluator for the
   subset the app actually needs: numeric literals, `x`/`X`, parentheses,
   arithmetic operators, and any explicitly supported functions such as
   `min`, `max`, or `clamp`.
2. Parse each transform once and evaluate against the current value without
   expression trees, reflection emit, or runtime generic type construction.
3. Add tests for valid transforms, invalid transforms, divide-by-zero,
   culture-invariant parsing, and the existing fallback-to-raw behavior.
4. If NCalc must remain, verify whether it has a strict interpreted mode that
   avoids the Parlot/FastExpressionCompiler compile paths. Do not assume this:
   the unmasked AOT log proves those compile paths are currently in the
   dependency closure.

## Warning group 5: FanControl LibreHardwareMonitor OpCode dynamic code

Warnings:

- `IL2026` from `Computer.Open()` calling annotated `OpCode.Open()` before the
  targeted fix.
- `IL3050` remains in other LibreHardwareMonitor code paths after the
  LibreHardwareMonitor Native AOT suppressions were removed.

Locations:

- Call site: `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Computer.cs:518`
- Formerly annotated method:
  `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/OpCode.cs:216`
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Cpu/Amd17Cpu.cs:26`
  uses `Enum.GetValues(Type)`.
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Motherboard/Lpc/EC/EmbeddedController.cs:511`
  uses `Enum.GetValues(Type)`.
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Gpu/IntelDiscreteGpu.cs:152`
  uses `Marshal.SizeOf(Type)`.
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Gpu/IntelDiscreteGpu.cs:303`
  uses `Marshal.SizeOf(Type)`.
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Gpu/IntelDiscreteGpu.cs:338`
  uses `Marshal.SizeOf(Type)`.
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Gpu/IntelDiscreteGpu.cs:349`
  uses `Marshal.SizeOf(Type)`.
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Interop/IntelGcl.cs:236`
  uses `Marshal.SizeOf(Type)`.

Cause:

`OpCode.Open()` allocates executable memory and builds delegates for CPUID and
RDTSC opcode stubs. The former warning was caused by the local
`RequiresUnreferencedCode` annotation. The method is not ordinary managed JIT
generation and does not depend on trim-sensitive managed reflection.

The post-removal build also exposed conventional AOT warnings in
LibreHardwareMonitor code where non-generic reflection-shaped APIs are used
despite generic AOT-safe alternatives existing.

Proposed fixes:

1. Done for the immediate warning: remove the incorrect
   `RequiresUnreferencedCode` annotation from `OpCode.Open()`. The targeted
   workaround publish no longer reports the `Computer.Open()`/`OpCode.Open()`
   warning.
2. For a stronger runtime design, replace this opcode-buffer approach with
   .NET hardware intrinsics or a tiny native helper compiled into the Native
   AOT build.
3. Keep the existing LibreHardwareMonitor implementation for other target
   frameworks if needed, but route the FanControl Native AOT build through the
   AOT-safe path.
4. Replace `Enum.GetValues(Type)` with `Enum.GetValues<TEnum>()` or
   `Enum.GetValuesAsUnderlyingType(Type)` where appropriate.
5. Replace `Marshal.SizeOf(Type)` with `Marshal.SizeOf<T>()` where the type is
   statically known.
6. Only use source-level suppression if runtime tests prove CPU sensor
   discovery works on AMD and Intel under the Native AOT executable.

## Warning group 6: FanControl System.Management and WMI/IPMI

Original warnings and diagnostics:

- `IL2067`
- `IL2077`
- `IL3052`
- ILC diagnostics that these constructors will always throw:
  - `System.Management.WbemObjectTextSrc..ctor()`
  - `System.Management.WbemStatusCodeText..ctor()`
  - `System.Management.WbemContext..ctor()`

Locations:

- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Motherboard/Lpc/Ipmi.cs:8`
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj:91`
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Memory/MemoryGroup.cs`

Cause:

LibreHardwareMonitor contained a direct `System.Management` WMI/IPMI path, and
the RAMSPDToolkit dependency pulled `System.Management` transitively for memory
SPD probing. Native AOT does not support the COM interop shape used by these
System.Management types, and the compiler reported that some constructors would
always throw.

Status:

Fixed for FanControl `net10.0` `Release` / `ReleaseWithDebugging` Native AOT
builds.

Resolution:

1. `DisableIpmiWmi=true` removes `Ipmi.cs`, skips IPMI probing, and removes the
   direct `System.Management` package reference.
2. `DisableRamSpd=true` removes RAMSPDToolkit files/package references and keeps
   `MemoryGroup` to virtual/total memory sensors only.
3. Debug builds keep the WMI/RAMSPDToolkit paths so development behavior is not
   changed.
4. If IPMI or DIMM SPD sensors are required in the Native AOT app later, move
   that functionality to a non-AOT helper process or replace it with an AOT-safe
   Windows API path.

## Warning group 7: FanControl DiskInfoToolkit and HidSharp

Warnings:

- `IL2091` in DiskInfoToolkit `PtrToStructure<T>` helpers.
- `IL2075` in HidSharp Linux `uname` reflection code that is still in the
  dependency closure.

Locations and dependency entry points:

- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs:9`
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj:84`
- HidSharp warning originates from package source path
  `C:\Code\src\oss\hidsharp\hid\HidSharp\Platform\Linux\NativeMethods.cs:134`.

Targeted trim-config result:

Native AOT ILC accepts `TrimmerRootDescriptor` files as `--descriptor`, but a
descriptor that preserved all of `DiskInfoToolkit` and `HidSharp` did not clear
these warnings. `TrimMode=partial` also did not clear them. These are analyzer
dataflow warnings in package method bodies, not missing metadata roots.

Proposed fixes:

1. For DiskInfoToolkit, replace generic `Marshal.PtrToStructure<T>` helpers
   with AOT-friendly `MemoryMarshal.Read<T>`/`Unsafe.ReadUnaligned<T>` where
   `T` is unmanaged, or add the correct
   `DynamicallyAccessedMembers` annotations upstream/local.
2. If storage sensors are not required for fan-control decisions, make
   `IsStorageEnabled` configurable and default it off for the Native AOT tray
   build. It is currently enabled in `LHMService.cs`.
3. For HidSharp, verify whether the Linux reflection path is unreachable in
   the Windows x64 publish. If it is unreachable, use a narrow package warning
   suppression with a comment. If it is reachable through generic HID code,
   switch to a Windows-only HID path for the Native AOT build.

## Suppression debt

The previously broad Native AOT suppressions have been removed:

- `VolumeTrayAppDotNET/src/VolumeTrayAppDotNET.csproj:39`
  formerly suppressed `IL2067;IL2104;IL3050;IL3053`.
- `FanControlTrayAppDotNET/src/FanControlTrayAppDotNET.csproj:42`
  formerly suppressed `IL2026;IL2055;IL2067;IL2072;IL2075;IL2077;IL2090;IL2091;IL2104;IL3050;IL3052;IL3053`.
- `FanControlTrayAppDotNET/LibreHardwareMonitor/LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj:35`
  formerly suppressed `IL2026;IL3050;IL2075;IL2070`.

The only remaining Native AOT `NoWarn` in app project files is the narrow
RuntimeIdentifier-only TraceEvent containment in `VolumeTrayAppDotNET.csproj`.
LibreHardwareMonitor also retains its existing `CA1416` platform-compatibility
suppression, which is not a Native AOT warning suppression.

Recommended policy:

1. Do not suppress AXAML resource dictionary warnings. Keep static AXAML
   dictionaries on typed compiled `ResourceDictionary` paths instead.
2. Keep package-level suppressions only when the warning is proven unreachable
   or runtime-tested safe.
3. Track remaining third-party package warnings as explicit work items instead
   of hiding them under one project-wide `NoWarn` list.
4. Add a CI/audit command that runs Native AOT with
   `-p:IlcTreatWarningsAsErrors=false -p:TrimmerSingleWarn=false` and archives
   the warning log, plus a second scheduled/manual pass with `-p:NoWarn=`.

## Suggested fix order

1. Fix FanControl Native AOT restore/publish orchestration so `build.ps1` and
   release tooling agree.
2. Fix DiskInfoToolkit `IL2091` and HidSharp `IL2075` locally/upstream, or
   narrow-suppress only after proving the warning paths unreachable in the
   Windows x64 Native AOT app.
3. Replace LibreHardwareMonitor `Enum.GetValues(Type)` and `Marshal.SizeOf(Type)`
   usages with the generic AOT-safe alternatives where possible.
4. Smoke-test Release Native AOT FanControl on target hardware and confirm that
   losing IPMI and DIMM SPD sensors in the Native AOT build is acceptable.
5. Remove broad `NoWarn` entries once each warning class is fixed, unreachable,
   or covered by a runtime smoke test.
