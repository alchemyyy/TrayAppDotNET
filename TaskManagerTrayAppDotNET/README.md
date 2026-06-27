# TaskManagerTrayAppDotNET

`TaskManagerTrayAppDotNET` is the beginning of a Windows 11 Task Manager reimplementation built on
`TrayAppDotNETCommon`.

## Implemented

- Shared TrayAppDotNET crash watcher, single-instance, install, startup, tray icon, theme, and custom window chrome.
- A `SettingsWindowCommon<TaskManagerPage>` shell with the stock Task Manager page structure.
- A functional Details page showing live process name, PID, status, approximate owner, CPU, private memory, and
  working-set memory.
- Task Manager-compatible Name/PID contains search plus typed column expressions, boolean operators, and regex.
- A live Lifetime column formatted as `1d 8:10:22` or `16:12:01`.
- Clickable sort headers and PID-stable row selection.
- Desktop and packaged-app icons in the visible Details rows.
- Native-first and managed-fallback `TerminateProcess` paths for End task.
- A preconstructed inline Run new task surface for executables, documents, and URIs.
- A tray menu that keeps the main window warm when it is hidden.
- A square dynamic tray graph with current and recency-weighted marquee styles for average CPU, highest-core CPU,
  or physical-memory utilization.

## Efficiency baseline

The Details list is one custom-painted Avalonia control. It creates no per-row controls or per-row view models,
uses arithmetic hit-testing, paints only the effective viewport plus one overscan row, and stores row order in a
preallocated integer array. Static formatted text caches are keyed by process identity, so inserting or sorting processes does
not invalidate every row after the insertion point.

Sampling runs on one below-normal-priority dedicated thread. Two fixed 8,192-row buffers are swapped under a short
lock, while the UI owns a third fixed buffer. UI notifications are coalesced to one pending dispatcher callback.
End task is independent of the sampler and pre-opens handles when a process is selected.

### Render-thread row hover

Process-row hover is a non-hit-testable `CompositionCustomVisual`, not a UI-thread pointer-event effect.
`ProcessDetailsCanvas` publishes immutable table geometry only when structural table state changes.
`ProcessRowHoverVisual` also resends its client origin and render scaling after arrange or DPI changes. Its composition
animation callback samples `GetCursorPos`, validates the owning HWND with `WindowFromPoint` and `GetAncestor`, converts
screen pixels through `ScreenToClient` and `TopLevel.RenderScaling`, and performs arithmetic row hit-testing on the
render thread. It invalidates only the old and new row rectangles and samples again immediately before painting.

This keeps decorative row feedback moving during a brief Avalonia UI-thread stall. Selection, clicks, header input,
and accessibility remain on the normal UI path. The sampling clock runs only while the window is visible and not
minimized. `GPUPreferred` is the default rendering backend; software rendering remains an explicit fallback option.
The implementation is in `src/UI/ProcessRowHoverVisual.cs`, with geometry produced by
`src/UI/ProcessDetailsCanvas.cs`. The full design and verification checklist are in
`../.agents/UI_TRAY_PLAYBOOK.md`.

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
End process tree routes each descendant through the same native-first path before terminating the root.

Process image paths and application IDs are resolved once when a process enters the sampler history. Icon extraction
is requested only for viewport rows, runs serially on the thread pool, and shares one bitmap per case-insensitive shell
identity. The UI cache is capped at 256 completed icons with at most 64 pending loads; eviction and bitmap disposal run
on the UI thread so rendering never races a disposed image.

Raster tiles like CharacterMapDotNET are not used yet. CPU and memory values make most visible rows dirty every
second, so tile invalidation would currently erase much of the benefit. The single-canvas boundary leaves room for
row strips or tile packing after measurement shows where raster caching wins.

Managed code cannot guarantee that selected instructions or data remain resident in L3 cache. The practical
baseline here is to keep the main window and critical controls constructed, avoid thread-pool dependency for
sampling, keep sampling below UI priority, and keep termination independent from enumeration. The native helper
does reserve a small nonzero working set in exchange for remaining dispatchable during severe memory pressure.

## Intentional shells

- Processes, Performance, App history, Startup apps, Users, Services, and Settings pages.
- Accurate token-derived user names, suspended-state detection, publisher data, and command-line collection.
- Column resizing, persistence, context menus, and process-tree operations.
- A native `NtQuerySystemInformation` sampler. The current `System.Diagnostics.Process` sampler is a bring-up
  backend and still allocates the process array and wrappers once per sample.

## Run

Plain text uses a case-insensitive contains search against Name and PID. Column expressions support `=`, `!=`, `<`,
`<=`, `>`, `>=`, `&&`, `||`, and parentheses. Column names use braces and offer mouse or arrow-key autocomplete;
Enter and Tab complete the selected column. For example, this selects processes between one and two hours old:

```text
{Lifetime}>=1h&&{Lifetime}<2h
```

Use `=~` and `!~` for case-insensitive .NET regex matching. Quote regex patterns when they contain expression
syntax:

```text
{Status}="Running"&&({Command line}=~"--type=(renderer|gpu-process)"||chrome)
```

Time operands accept `ms`, `s`, `m`/`min`, `h`, and `d`. Memory and byte operands accept binary `k`, `m`, `g`, and
`t` suffixes. Other numeric columns accept decimal magnitude suffixes, and percentage columns accept `%`.

```powershell
dotnet run --project .\TaskManagerTrayAppDotNET\src\TaskManagerTrayAppDotNET.csproj -- --monitored
```

Set `TrayAppDotNET_NO_WATCHER=1` when launching without the crash watcher.

The elevated mailbox smoke test is opt-in because it can display UAC:

```powershell
$env:TASK_MANAGER_RUN_ELEVATED_KILL_HELPER_TEST = '1'
dotnet test .\tests\TaskManagerTrayAppDotNET.Tests\TaskManagerTrayAppDotNET.Tests.csproj `
  --filter 'FullyQualifiedName~ElevatedKillHelperSmokeTests'
```

To run the same mailbox test while normal-priority workers saturate every logical processor:

```powershell
.\tests\run_elevated_kill_helper_cpu_stress.ps1
```
