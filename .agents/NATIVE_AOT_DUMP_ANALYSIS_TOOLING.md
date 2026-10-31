# Native AOT Dump Analysis

This documents repeatable capture and analysis for Native AOT tray hangs and
memory leaks. The hang case study covers a Volume tray dispatcher failure. The
memory case studies cover a Brightness WMI/COM leak caused by an invalid
source-generated COM release path, and Brightness memory that a closed settings
window left behind in an idle app.

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

### One-Chance Memory Capture

When the process is in a rare high-memory state and only one capture is allowed,
record process identity and counters before taking the dump. Dump capture can
page memory into the working set, so post-capture working set is not a valid
baseline.

```powershell
$processID = <APP_PID>
$process = Get-Process -Id $processID
$cimProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $processID"

$process | Select-Object Id, ProcessName, StartTime, HandleCount, Threads,
    WorkingSet64, PrivateMemorySize64, VirtualMemorySize64
$cimProcess | Select-Object ExecutablePath, CommandLine, ParentProcessId
```

Use one direct full user-mode ProcDump capture. Run
`tools\download_procdump.ps1` from the repository root to download the
reviewed official executable into `tools\`, where `.gitignore` excludes it.
Alternatively, place an official ProcDump installation on PATH or pass its local
path to the applicable capture script. Dumps go under the ignored `dumps\`
folder. Do not commit or redistribute the ProcDump
executable with this repository. Do not configure repeated dumps for a
one-chance incident.

```powershell
procdump64.exe -accepteula -ma $processID ".\dumps\App_${processID}_full.dmp"
```

Verify the resulting file directly instead of assuming a nonzero ProcDump exit
code means capture failed. ProcDump can report its dump-count termination after
success.

```powershell
Get-Item -LiteralPath ".\dumps\App_${processID}_full.dmp" |
    Select-Object FullName, Length, LastWriteTime
```

When ProcDump is unavailable (for example `tools\download_procdump.ps1` rejects
a changed download hash; do not bypass the check), CDB writes the same full
dump from a noninvasive attach, which suspends the process only while writing:

```powershell
& 'C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe' -pv -p $processID -c ".dump /ma .\dumps\App_${processID}_full.dmp; qd"
```

Do not leave an invasive CDB session with breakpoints on a live tray app. If the
debugger is killed while the target is broken in, every thread keeps an extra
suspend count and the `int 3` bytes stay patched in, so the app freezes for good.
To recover, attach noninvasively, restore the original byte with `eb`, run
`~*m` to drop the extra suspend count, then `qd`. Use ETW (below) for allocation
stacks instead.

Preserve these items together:

- the dump
- pre-capture process metadata
- the exact Native AOT PDB matching the installed executable
- debugger command output
- the active application log covering the leak interval

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

## Analyze Native Heap Growth

`dotnet-dump analyze` can report that no CLR runtime is present for a Native AOT
dump. That does not invalidate the dump. Use CDB with the matching native PDB and
inspect virtual memory, NT heaps, handles, and native object vtables.

Start with:

```text
!address -summary
!heap -s
!handle 0 0
~* kP
```

Identify the large committed heap from `!heap -s`, then list its dominant block
sizes:

```text
!heap -stat -h <HEAP_ADDRESS>
```

Filter suspicious size classes and symbolize the first pointer-sized field of
each allocation. COM and RPC objects commonly begin with a vtable pointer.

```text
!heap -flt s <BLOCK_SIZE_HEX>
dps <ALLOCATION_ADDRESS> L1
```

Repeat the filter for the dominant size classes. A coherent family of vtables is
more useful than a single large allocation. For example, the Brightness leak had
matching multiples of these types:

```text
combase!CStdIdentity::vftable
combase!CClientChannel::vftable
combase!CIDObject::vftable
combase!CChannelHandle::vftable
rpcrt4!LRPC_CCALL::vftable
rpcrt4!LRPC_BINDING_HANDLE::vftable
fastprox!CWbemSvcWrapper::vftable
```

That combination identifies accumulated WMI client proxies and their COM/RPC
channel state. Correlate the repeated-object count with logs and retry cadence
before assigning the source trigger.

### Source-Generated COM Release Trap

Do not expose and call `StrategyBasedComWrappers.ReleaseObjects` as a supposed
deterministic release mechanism. The .NET 10 implementation is sealed and throws
`NotImplementedException`:

```text
https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Runtime.InteropServices/src/System/Runtime/InteropServices/Marshalling/StrategyBasedComWrappers.cs
```

`UniqueComInterfaceMarshaller<T>` creates `UniqueInstance` `ComObject` RCWs.
Release those through `ComObject.FinalRelease()`. Use
`Marshal.FinalReleaseComObject` only for built-in RCWs. Never manually release a
pointer owned by a still-finalizable `ComObject`; that can double-release later.

The regression test for this contract is:

```text
BrightnessTrayAppDotNET/tests/BrightnessTrayAppDotNET.Tests/COMActivationTests.cs
```

The safe WMI enumeration stress probe is:

```powershell
dotnet run --project .\BrightnessTrayAppDotNET\tests\WindowsBrightnessProbe\WindowsBrightnessProbe.csproj -- --enumeration-stress 5000
```

This mode does not change brightness. It reports private bytes and handle counts
before and after repeated full enumeration.

## Idle Memory After A Closed Window

Symptom: Brightness under the GPU backend idled at about 130 MB private bytes
fresh, but stayed at about 250 MB after the settings window had been opened,
browsed and closed. Nothing grew afterwards; the memory just never came back.
The Software backend idles at about 22 MB and rose to about 71 MB after the
same sequence.

Two independent causes, found by comparing a dump of the inflated process with
one of a fresh process:

- The GC heap held about 40 MB of the closed window's garbage. An idle tray app
  allocates too little to trigger the full collection that would free it.
- About 15 private blocks of 7.2 MB each (`0x730000`, zero-filled, reserve then
  commit) had no NT heap owner. An ETW trace showed them as `MEM_ROTATE`
  allocations from `D3DKMTCreateAllocation`, created through
  `EGL_CreatePbufferFromClientBuffer` in
  `AngleD3DTextureFeature.RenderTargetWrapper.BeginDrawCore`: Avalonia's
  WinUI-composition path wraps every frame's DComp texture in a new ANGLE pbuffer.
  The driver caches the released allocations and trims that cache only while the
  device keeps rendering, so an app that goes idle keeps it.

Native AOT has no SOS, but the GC heap can still be inspected from a full dump:
walk each committed region of the GC reservation (the one sized five times the
heap hard limit) object by object, reading the `MethodTable` at each object start
(component size `u16` at +0, flags `u16` at +2, base size `u32` at +4; flag
`0x8000` adds a component count read at object +8, flag `0x0100` means the type
has references). Symbolize `MethodTable` addresses with CDB `ln`; they resolve
to names ending in ``::`vftable'`` in the matching PDB. A per-type count compared
against a fresh dump shows what accumulated. For reachability, follow the
`GCDesc` series stored before each `MethodTable`. Take roots from the
`__GCSTATICS` slots in the exe image and from strong and pinned handles in the
64 KB handle-table segments (block types at segment offset `0x618`). Weak
handles do not root. Conservative edges invent paths through pooled arrays,
which are allocated uninitialized and keep stale pointers.

For unknown native allocations, record kernel VirtualAlloc events with stacks,
reproduce, then dump and filter by process and size:

```powershell
xperf -on PROC_THREAD+LOADER+VIRT_ALLOC -stackwalk VirtualAlloc -BufferSize 1024 -MaxBuffers 512 -FileMode Circular -MaxFile 1024 -f kernel.etl
xperf -stop -d merged.etl
xperf -i merged.etl -o dump.txt -tle -tti -a dumper -stacktimeshifting
```

Symbolize the user-mode frames with `ln` against a dump of the same process.

The fix is `TrayAppDotNETCommon/src/UI/TrayAppDotNETMemoryRelease.cs`. Every
settings window records its EGL display through a 1x1 composition custom visual
that leases the Skia platform graphics context. Once a settings window has
closed and the compositor has processed the batch that disposes its render
target, a background task runs `ManagedHeap.CompactAndDecommit` and then
`IDXGIDevice3::Trim` on the ANGLE device, holding the EGL display lock that
ANGLE contexts share. Measured on the Release build: open, browse and close
went from 235 MB to 120 MB private bytes (fresh baseline 131 MB). The GC alone
brought 230 MB to 188 MB; the trim released the remaining 50 MB of driver blocks.

A closed window can also stay reachable after its content was severed. Avalonia
12.1.1's `ToolTipService` keeps the last tooltip host in `_tipControl` until the
pointer moves over another tooltip host, and UI Automation clients pin
`AutomationNode` wrappers through refcounted handles. Exclude UIA-driven test
runs when you attribute retention.

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
