# TaskManagerTrayAppDotNET

`TaskManagerTrayAppDotNET` is a Windows 11 Task Manager reimplementation built on `TrayAppDotNETCommon`, part of
[TrayAppDotNET](../README.md). It is a regular window with a live tray graph, and it keeps running in the tray when
the window is closed.

User documentation: [documentation/docs/apps/task-manager](../documentation/docs/apps/task-manager/index.md)
([Processes](../documentation/docs/apps/task-manager/processes.md),
[Performance](../documentation/docs/apps/task-manager/performance.md),
[Other Pages](../documentation/docs/apps/task-manager/other-pages.md)). Building: see the
[root README](../README.md#development).

## Implemented

- Shared TrayAppDotNET crash watcher, single-instance, install, startup, tray icon, theme, and custom window chrome.
- The Task Manager page structure: Processes, Performance, App history, Startup apps, Users and Services, plus a
  separate settings window.
- A combined processes and details table with grouping, zoom and stretch, live column totals, multi-select, column
  customization, saved searches and a search with typed column expressions, boolean operators and regex.
- Native-first and managed-fallback `TerminateProcess` paths for End task and End process tree.
- A preconstructed inline Run new task surface for executables, documents, and URIs.
- Performance graphs for the CPU, memory, GPUs, network adapters and disks, recorded in the background.
- Optional replacement of Windows Task Manager (needs the system-wide install) and of the Ctrl+Shift+Esc shortcut.
- A tray menu that keeps the main window warm when it is hidden.
- A rounded dynamic tray graph with current and recency-weighted marquee styles for average CPU, highest-core CPU,
  or physical-memory utilization.

## Efficiency baseline

The Processes table is one custom-painted Avalonia control. It creates no per-row controls or per-row view models,
uses arithmetic hit-testing, paints only the effective viewport plus one overscan row, and stores row order in a
preallocated integer array. Static formatted text caches are keyed by process identity, so inserting or sorting processes does
not invalidate every row after the insertion point.

Process sampling runs on one dedicated normal-priority thread (below-normal starves whenever normal-priority work
saturates every core). Each refresh is one `NtQuerySystemInformation(SystemProcessInformation)` query whose records are
read in place from the native buffer, at most 8,192 rows. Two fixed row buffers are swapped under a short lock, while
the UI owns a third fixed buffer. UI notifications are coalesced to one pending dispatcher callback. End task is
independent of the sampler and pre-opens handles when a process is selected.

Performance sampling runs on its own thread for the app's lifetime at the configured interval (250 ms to 60 s; the
history holds at most 36,000 samples). PDH counters are set up lazily on that thread and retried with backoff when
setup fails; the CPU sample covers every processor group; the D3D12 feature level is probed once per adapter without
creating a device; physical-memory metadata is read from WMI in a background task with bounded waits and retries.

### Render-thread hover

Process-row hover is a non-hit-testable `CompositionCustomVisual`, not a UI-thread pointer-event effect.
`ProcessTableCanvas` publishes immutable table geometry only when structural table state changes.
`ProcessRowHoverVisual` also resends its client origin and render scaling after arrange or DPI changes. Its composition
animation callback samples `GetCursorPos`, validates the owning HWND with `WindowFromPoint` and `GetAncestor`, converts
screen pixels through `ScreenToClient` and `TopLevel.RenderScaling`, and performs arithmetic row hit-testing on the
render thread. It invalidates only the old and new row rectangles and samples again immediately before painting.

This keeps decorative row feedback moving during a brief Avalonia UI-thread stall. Selection, clicks, header input,
and accessibility remain on the normal UI path. The sampling clock runs only while the window is visible and not
minimized. `GPUPreferred` is the default rendering backend; software rendering remains an explicit fallback option.
The implementation is in `src/UI/ProcessesPage/ProcessRowHoverVisual.cs`, with geometry produced by
`src/UI/ProcessesPage/ProcessTableCanvas.cs`. The full design and verification checklist are in
`../.agents/UI_TRAY_PLAYBOOK.md`.

Performance graphs draw their hover line and metric label the same way, through
`src/UI/Performance/PerformanceGraphHoverPresenter.cs`. Each graph's sampling clock runs only after the UI thread sees
the pointer enter its hover area, which includes a logical-processor graph's trailing hit-test spacing. Movement and
exit are then handled on the render thread: the line snaps to the nearest sample (or follows the pointer on the disk
and GPU memory graphs), and the label and the gap the line leaves around it move with every frame. Text cannot be
shaped on the render thread, so the UI thread lays out the label for its own latest pointer sample and hands over its
glyph runs; during fast movement the label's number can trail the line by the UI thread's latency.

## Termination path

Normal app startup creates an anonymous 4 KB shared section and two auto-reset events, then launches the native
helper at standard integrity. Enabling elevated termination replaces that process by launching the same
`TaskManagerTrayAppDotNET.exe` through UAC with `--kill-helper`. A custom NativeAOT `wmain` dispatches helper mode
to the linked C++ implementation before calling `__managed__Main`, so the helper process never initializes .NET
or its GC. The helper duplicates the handles from the parent rather than exposing named kernel objects. Its single
request thread blocks indefinitely in `WaitForMultipleObjects`, so it has no polling timer or idle CPU wakeup.

Debug and other managed builds retain `TaskManagerTrayAppDotNET.KillHelper.exe` as a standalone development
fallback. NativeAOT release and installed payloads contain no helper sidecar.

The helper enables `SeDebugPrivilege` when its token contains it, enters the high priority class, disables
execution-speed throttling, and raises its minimum working-set quota to 512 KB before using `VirtualLock`. The
pinned ranges include the mailbox, critical state, the complete dedicated `.killhot` linker section, imported
kernel entry-point pages, and the fully committed 128 KB request-thread stack.
Selecting a row arms an exact PID-plus-creation-time identity and pre-opens process handles in the native helper
and managed process. End task waits briefly for the native helper result, then uses `TerminateProcess` through the
managed pre-opened handle only if the native request fails or times out. The helper rejects the app, itself, changed
process identities, and processes Windows marks critical. Declining UAC retains the standard helper. Losing either
helper falls back to the managed path for that request and starts a standard-integrity replacement.
End process tree routes each descendant through the same native-first path before terminating the root. The tree is
collected from one `NtQuerySystemInformation` snapshot, and every parent/child edge must show the child created after
its parent; an edge that fails (a recycled parent PID) drops that child and everything below it, the same rule the
displayed tree uses. The helper refuses a target whose creation time is unknown.

Process image paths and application IDs are resolved once when a process enters the sampler history. Icon extraction
is requested only for viewport rows, runs serially on the thread pool, and shares one bitmap per case-insensitive shell
identity. The UI cache is capped at 256 completed icons with at most 64 pending loads; eviction and bitmap disposal run
on the UI thread so rendering never races a disposed image.

Raster tiles like CharacterMapDotNET are not used yet. CPU and memory values make most visible rows dirty every
second, so tile invalidation would currently erase much of the benefit. The single-canvas boundary leaves room for
row strips or tile packing after measurement shows where raster caching wins.

Managed code cannot guarantee that selected instructions or data remain resident in L3 cache. The practical
baseline here is to keep the main window and critical controls constructed, avoid thread-pool dependency for
sampling, keep sampling off the UI thread, and keep termination independent from enumeration. The native helper
does reserve a small nonzero working set in exchange for remaining dispatchable during severe memory pressure.

## Elevation

The app runs `asInvoker`. Five separate channels touch other integrity levels or other processes:

- **Privileged-action broker** (`--elevated-broker`, `src/Services/ElevationBroker*.cs`): launched with `runas` on the
  first admin action. Its command line names the UI's PID and start time; it verifies them, keeps a handle to the UI,
  and serves only a pipe client with that PID, one connection, then exits. Requests are a closed op set validated on
  the elevated side (startup approval writes only reach the three `Explorer\StartupApproved` subkeys with a named
  value) and every served op is logged. The broker writes no log file: its lines travel to the UI over the pipe and
  land in the UI's log. It creates no files either: for a memory dump the UI creates the empty file in its own temp
  folder and sends the handle value; the broker duplicates it from the pinned UI process (`PROCESS_DUP_HANDLE`),
  checks it is an empty regular disk file and writes the dump into it. The UI side uses asynchronous pipe I/O with
  per-operation timeouts; a timed-out request is reported as possibly still completing. The full design is in
  `../documentation/plantuml/tmtadn/elevated_broker_spec.md`.
- **Kill helper** (above): anonymous section and events whose handle values go on the helper command line; the helper
  duplicates them from its parent. An elevated Task Manager starts the standard helper directly so it inherits the
  elevated token.
- **Activation pipe** (`src/Services/TaskManagerActivationServer.cs`): lets a redirected `taskmgr.exe` launch bring the
  running window forward. The name carries the user SID and session, the DACL and owner are the user SID, the client
  connects at identification level and only trusts a pipe owned by its own user.
- **Always run as administrator task** (`src/Services/ElevatedStartTask.cs`, COM client in
  `src/Interop/TaskScheduler*.cs`): one scheduled task per account, `\TrayAppDotNET\TaskManagerTrayAppDotNET-<SID>`,
  that starts the Program Files system install with an interactive-token principal at the `HighestAvailable` run
  level. The settings toggle registers or deletes it through an elevated `--set-elevated-start on|off` relaunch
  (`ElevatedSettingChange`, shared with `--set-taskmgr-replacement`). The task's security descriptor lets the account
  read and run it but only SYSTEM and administrators change or delete it, so an unelevated plain start (no
  arguments, `--autostart`, `--hidden`, or a redirected `taskmgr.exe` start) runs it with its start flags as `$(Arg0)`
  and exits, but only after checking that its definition still starts this exe elevated. Any program running as the
  user can run the task and choose `$(Arg0)`, so `Program.Main` checks for `--elevated-start-task` before every other
  mode and accepts only `--autostart` and `--hidden` after it; anything else exits with code 2. Registration only ever
  targets the system install, because a copy in the user's profile can be replaced by any program the user runs. A
  system uninstall deletes every account's task (`TaskManagerUninstallGuard.BeforePrepare`). A system install also
  registers or deletes it for the installing account when the install options carry `RunAsAdministrator`
  (`--run-as-administrator true|false`): `AppServices` hands `ElevatedStartTask.SetEnabledForCurrentUser` to Common as
  a `TrayAppDotNETRunAsAdministratorOption`, which the elevated install runs after the files are copied. The app's
  installer window and the bundle installer show it checked by default with a warning.
- **Interactive-user launches** (`src/Services/InteractiveUserLauncher.cs`): Run new task, the Services and Users page
  shortcuts and Restart explorer's fresh shell call wdc.dll's `WdcRunTaskAsInteractiveUser`, which Windows Task
  Manager's Create new task calls when its administrator box is off. It runs the stock
  `\Microsoft\Windows\Task Manager\Interactive` task in the app's session, and that task's COM handler opens the command
  with the Run dialog's `ShellExecCmdLine` as the interactive user. An elevated app never hands its token on, and
  neither path needs Explorer running.

## Known gaps

- App history is collected only while its page is on screen and is kept in memory only.
- The App history Notifications column stays empty, and Startup impact on the Startup apps page is always
  "Not measured".
- The table, tray and window key and mouse bindings are fixed; the Hotkeys settings page lists them read-only.

## Process search

Plain text uses a case-insensitive contains search against Name and PID. Column expressions support `=`, `!=`, `<`,
`<=`, `>`, `>=`, `&&`, `||`, and parentheses. Column names use braces and offer mouse or arrow-key autocomplete;
Enter and Tab complete the selected column. For example, this selects processes between one and two hours old:

```text
{Lifetime}>=1h&&{Lifetime}<2h
```

Use `=~` and `!~` for case-insensitive .NET regex matching. Quote regex patterns when they contain expression
syntax:

```text
{Status}!="Suspended"&&({Command line}=~"--type=(renderer|gpu-process)"||chrome)
```

Time operands accept `ms`, `s`, `m`/`min`, `h`, and `d`. Memory and byte operands accept binary `k`, `m`, `g`, and
`t` suffixes. Other numeric columns accept decimal magnitude suffixes, and percentage columns accept `%`.

## Running from source

```powershell
dotnet run --project .\TaskManagerTrayAppDotNET\src\TaskManagerTrayAppDotNET.csproj -- --monitored
```

Set `TrayAppDotNET_NO_WATCHER=1` when launching without the crash watcher.

The elevated mailbox smoke test is opt-in because it can display UAC:

```powershell
$env:TASK_MANAGER_RUN_ELEVATED_KILL_HELPER_TEST = '1'
dotnet test .\TaskManagerTrayAppDotNET\tests\TaskManagerTrayAppDotNET.Tests\TaskManagerTrayAppDotNET.Tests.csproj `
  --filter 'FullyQualifiedName~ElevatedKillHelperSmokeTests'
```

To run the same mailbox test while normal-priority workers saturate every logical processor:

```powershell
.\TaskManagerTrayAppDotNET\tests\run_elevated_kill_helper_cpu_stress.ps1
```

The Task Scheduler smoke test registers, runs and deletes tasks in a temporary `\TrayAppDotNET.Tests-<guid>` folder.
Registering a task that runs elevated needs an elevated test run, and its probe task flashes a console window:

```powershell
$env:TASK_MANAGER_RUN_SCHEDULED_TASK_TEST = '1'
dotnet test .\TaskManagerTrayAppDotNET\tests\TaskManagerTrayAppDotNET.Tests\TaskManagerTrayAppDotNET.Tests.csproj `
  --filter 'FullyQualifiedName~ScheduledTaskSmokeTests'
```
