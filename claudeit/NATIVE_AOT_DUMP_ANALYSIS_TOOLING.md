# Native AOT Tray Hang Dump Analysis

This documents the process used to diagnose the rare tray lockup where the
Volume tray icon stayed visible but stopped responding to tooltip, flyout, and
context-menu interaction.

## Symptom

The process stayed alive, but the tray icon message path was dead:

- no tooltip
- no context menu
- no flyout
- no visible crash dialog
- process still present under Task Manager

That combination usually means the tray message thread is no longer pumping
messages. In this case it was not a CoreAudio deadlock. It was an unhandled
Avalonia dispatcher exception on the tray/UI thread under Native AOT.

## Locate The Running App

Use WMI/CIM instead of only `Get-Process` so command-line arguments identify
the monitored child process versus the watcher process.

```powershell
Get-CimInstance Win32_Process |
    Where-Object { $_.Name -eq 'VolumeTrayAppDotNET.exe' } |
    Select-Object ProcessId, ParentProcessId, CommandLine
```

The process of interest is the monitored app process, not the watcher:

```text
VolumeTrayAppDotNET.exe --monitored --watcher-pid <WATCHER_PID>
```

## Check The Active Log

The active log helped establish that the process was alive and that a watcher
poll exception happened near the freeze window.

```powershell
Get-Content -LiteralPath "$env:LOCALAPPDATA\TrayAppDotNET\VolumeTrayAppDotNET\active.log" -Tail 200
```

The important part was not the watcher exception itself. The log showed there
was no clean shutdown and no normal recovery after the UI stopped responding.

## Capture A Full Dump

Create a dump directory in the workspace:

```powershell
New-Item -ItemType Directory -Force -Path .\dumps
```

Capture the monitored process:

```powershell
dotnet-dump collect -p <APP_PID> -o .\dumps\VolumeTrayAppDotNET_<APP_PID>_dotnet.dmp
```

This produced the usable dump. A `comsvcs.dll` dump attempt produced a zero-byte
file in this case, so `dotnet-dump collect` was the reliable capture path.

## Install Command-Line Debugger

WinDbg Preview is fine interactively, but `cdb.exe` is better for repeatable
terminal analysis.

```powershell
choco install windows-sdk-10-version-2004-windbg -y --no-progress
```

Expected debugger path:

```text
C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe
```

## Load The Dump

```powershell
& 'C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe' -z .\dumps\VolumeTrayAppDotNET_<APP_PID>_dotnet.dmp
```

Inside `cdb`, configure symbols and add the Native AOT PDB directory.

```text
.symfix
.sympath+ C:\Users\Alchemy\Desktop\@Workbench\@TRAY_APP_DOT_NET\VTADN\VolumeTrayAppDotNET\bin\Release\win-x64\native
.reload /f
lm m VolumeTrayAppDotNET
```

For Native AOT, the matching PDB matters. If the module only shows raw offsets
or no useful app frames, find the matching PDB from the build that produced the
running EXE:

```powershell
Get-ChildItem -Path . -Recurse -Filter VolumeTrayAppDotNET.pdb |
    Sort-Object LastWriteTime -Descending |
    Select-Object FullName, LastWriteTime, Length
```

## Inspect All Thread Stacks

In `cdb`:

```text
~* kP
```

Then inspect the suspect UI/tray thread directly:

```text
~~[<OS_TID_HEX>]s
kP
```

The dump showed the tray/UI thread in this shape:

```text
ntdll!NtDelayExecution
KERNELBASE!SleepEx
VolumeTrayAppDotNET!S_P_CoreLib_System_Threading_Thread__Sleep
VolumeTrayAppDotNET!S_P_CoreLib_System_RuntimeExceptionHelpers__SerializeCrashInfo
VolumeTrayAppDotNET!S_P_CoreLib_System_RuntimeExceptionHelpers__FailFast
VolumeTrayAppDotNET!S_P_CoreLib_System_Runtime_EH__UnhandledExceptionFailFastViaClasslib
VolumeTrayAppDotNET!S_P_CoreLib_System_Runtime_EH__FailedAllocation
VolumeTrayAppDotNET!Avalonia_Base_Avalonia_Collections_AvaloniaList_1<System___Canon>__NotifyAdd
VolumeTrayAppDotNET!Avalonia_Base_Avalonia_Collections_AvaloniaList_1<System___Canon>__InsertRange
VolumeTrayAppDotNET!Avalonia_Controls_Avalonia_Controls_Panel__ChildrenChanged
VolumeTrayAppDotNET!VolumeTrayAppDotNET_UI_Flyout_VolumeFlyoutWindow__BuildDeviceTitleRow
VolumeTrayAppDotNET!VolumeTrayAppDotNET_UI_Flyout_VolumeFlyoutWindow__BuildDeviceRow
VolumeTrayAppDotNET!VolumeTrayAppDotNET_UI_Flyout_VolumeFlyoutWindow__BuildCell
VolumeTrayAppDotNET!VolumeTrayAppDotNET_UI_Flyout_VolumeFlyoutWindow__Rebuild
VolumeTrayAppDotNET!Avalonia_Base_Avalonia_Threading_DispatcherOperation__InvokeCore
```

The important frames are:

- `UnhandledExceptionFailFastViaClasslib`
- `FailedAllocation`
- `AvaloniaList.NotifyAdd`
- `VolumeFlyoutWindow.Rebuild`
- `DispatcherOperation.InvokeCore`

That means the failure happened inside an Avalonia dispatcher callback while
building the flyout visual tree. Under Native AOT, the unhandled managed
exception entered the fail-fast path. The thread then sat in crash-info
serialization/sleep instead of continuing to pump tray messages.

## Confirm The Tray Thread Was Dead

The visible process alone is not enough. The tray HWND thread must answer
messages. A quick HWND probe can send `WM_NULL` with a timeout to windows owned
by the process. In this incident, every Avalonia/tray window on the tray thread
timed out, including the hidden tray icon window class:

```text
VolumeTrayAppDotNET.TrayIcon.<GUID>
```

That confirmed the tray message thread was non-responsive, matching the user's
symptoms.

## Conclusion

The lockup was not caused by CoreAudio blocking the UI thread. The dump proved
the UI/tray thread had entered Native AOT fail-fast handling after an unhandled
dispatcher exception during `VolumeFlyoutWindow.Rebuild`.

Because the tray icon is message-loop driven, once that thread stopped pumping
messages the process could remain alive while the tray icon became permanently
non-responsive.

## Fix Pattern

Two layers were needed.

### Common Dispatcher Guard

Wire `Dispatcher.UIThread.UnhandledException` once in common startup and mark
the exception handled after logging. This prevents a dispatcher callback failure
from killing the tray message thread.

Implemented in:

```text
TrayAppDotNETCommon/src/UI/TrayAppDotNETAvalonia.cs
TrayAppDotNETCommon/src/CrashHandler.cs
```

### Safe Flyout Rebuilds

For flyouts that rebuild their visual tree from background state changes:

- wrap rebuild entry points in `try/catch`
- log locally
- coalesce rebuild requests
- defer hidden warm-window rebuild churn until the next show
- build new content before replacing old content where practical
- avoid letting event-driven rebuild failures escape the dispatcher

Applied to:

```text
VolumeTrayAppDotNET/src/UI/Flyout/VolumeFlyoutWindow.cs
BatteryTrayAppDotNET/src/UI/Flyout/BatteryFlyoutWindow.cs
BrightnessTrayAppDotNET/src/UI/Flyout/BrightnessFlyoutWindow.cs
FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.cs
```

Network was audited and did not have the same full flyout rebuild loop. It is
covered by the common dispatcher guard.

## Verification Commands

Build Debug serially to avoid duplicate intermediate-output file locks from
solution-level parallel builds:

```powershell
dotnet build TrayAppDotNET.slnx -c Debug -p:SkipKillRunningInstance=true -m:1
```

Build Release with Native AOT publish:

```powershell
dotnet build TrayAppDotNET.slnx -c Release -p:SkipKillRunningInstance=true -m:1
```

Run tests without rebuilding:

```powershell
dotnet test TrayAppDotNET.slnx -c Debug --no-build -m:1
```

## Notes

- Analyze the monitored child process, not the watcher.
- Keep the dump and matching PDB from the same build.
- A live process with a dead tray icon usually means the HWND owner thread is
  wedged, not necessarily that the whole process is deadlocked.
- Native AOT dumps are useful, but only if symbols match the native image.
