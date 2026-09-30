# Task Manager process status

The TMTADN Processes page Status column reports the three states the Windows 11
Task Manager Processes page reports: Suspended, Efficiency mode, and Not
responding. A process in none of those states shows an empty cell. Every rule on
this page was read from the installed `Taskmgr.exe` 10.0.26100.8972 with
Microsoft's public symbols, and TMTADN queries the same operating-system state.
The places where TMTADN deliberately differs are listed at the end.

[Status rules](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Models/ProcessStatus.cs)
[Hung-window scan](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessHungWindowCollector.cs)
[Efficiency query](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/NativeProcessInfo.cs)
[Sampler](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessSnapshotService.cs)
[Tests](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests/ProcessStatusTests.cs)

## Display

Right-click the Status header to choose **Status display**: Glyphs (the default)
or Text. The choice applies to every status, so a row never shows text and a
glyph together. Windows mixes them: a glyph for efficiency mode and for suspended
UWP apps, but text for a suspended desktop process and for Not responding.

| Status | Text | Glyph (Segoe Fluent Icons) | Glyph color |
| --- | --- | --- | --- |
| Suspended | `Suspended` | U+F2D9 CirclePause | `#FCE100` |
| Efficiency mode | `Efficiency mode` | U+F1E8 LeafTwo | `#6CCB5F` |
| Not responding | `Not responding` | U+E783 Error | `#FF99A4` |

- The texts are Task Manager's own strings.
- The suspended and efficiency glyphs and colors are the ones
  `AtmViewItem::SetStatusColumnTextAndIcon` draws in dark mode, and they are right-aligned
  as in Task Manager.
- Windows draws no glyph for Not responding. TMTADN uses the Fluent Error glyph in the
  Fluent critical color.
- The colors live in `TaskManagerWindow.axaml` and hot-reload in Debug builds.

In glyph mode, hovering a glyph shows its status text as a tooltip, and the
**Center glyphs** option centers each glyph in the column instead of aligning it
right. The option is dimmed while Text is selected.

Copying, searching, and sorting always use the text:

- `{Status}="Suspended"` finds suspended processes.
- `{Status}=""` finds processes with no status.
- An ascending sort lists Not responding, then Efficiency mode, then Suspended, then
  rows with no status.

## Suspended

TMTADN reads the thread array from the
`NtQuerySystemInformation(SystemProcessInformation)` snapshot the sampler already
takes. A process is suspended when every one of its threads is waiting with wait
reason `Suspended` (KWAIT_REASON 5). A process with no threads is never
suspended.

This matches both kinds of suspension:

- a process suspended with `NtSuspendProcess` or by a debugger;
- a packaged app frozen by Process Lifetime Management, whose frozen threads wait with
  the same reason.

On the development machine, the frozen packaged apps `SystemSettings`,
`CHXSmartScreen` and `ShellExperienceHost` and a suspended `vshost.exe` all
satisfied it.

The check costs no extra system call.

## Efficiency mode

A process is in efficiency mode when both of these hold. This is the test in
Task Manager's `UpdateEcoMode`.

1. EcoQoS is on. The process power-throttling state has
   `PROCESS_POWER_THROTTLING_EXECUTION_SPEED` set in both `ControlMask` and `StateMask`.
2. The priority class is `IDLE_PRIORITY_CLASS`.

Task Manager's own Efficiency mode command sets both. `EcoMode::ThrottleProcess`
calls `SetPriorityClass(IDLE)`, then `SetProcessInformation(ProcessPowerThrottling, {1, 1, 1})`.
Chromium-based browsers set both for their background processes.

EcoQoS alone does not qualify. On the development machine, 32 processes qualified:
28 Brave, 2 Chrome, 1 WebView2 and 1 Steam WebHelper, the same groups that show
the leaf in Task Manager. Four more processes had EcoQoS without the idle
priority class and correctly show nothing:

- SearchFilterHost;
- SearchProtocolHost;
- one svchost;
- WmiPrvSE.

TMTADN reads both values with `GetProcessInformation(ProcessPowerThrottling)` and
`GetPriorityClass` on the handle the sampler opens for the process. Task Manager
reads the same state through `NtQueryInformationProcess(ProcessPowerThrottlingState)`.

### Power throttling column

The Power throttling column reports this same state: Enabled for efficiency mode,
Disabled otherwise. Task Manager's Details page does the same;
`WdcProcessMonitor::UpdateProcess` fills its Power throttling cell from
`EcoMode::IsProcessThrottled`, the efficiency-mode map built by `UpdateEcoMode`.
EcoQoS alone therefore shows Disabled. The column shows N/A when either query fails.

## Not responding

While Status is active, the sampler enumerates the desktop's top-level windows
with `EnumWindows` once per refresh.

### Which windows count

A window counts when it passes Task Manager's
`WdcWindowMonitor::ShouldIncludeDesktopWindow` rule:

- It is visible.
- It is not `WS_EX_TOOLWINDOW` or `WS_EX_NOACTIVATE`, unless it is `WS_EX_APPWINDOW`.
- It has no visible owner, unless it is `WS_EX_APPWINDOW`.
- It is not the taskbar, `Shell_TrayWnd`.
- It has not been ghosted: `GhostWindowFromHungWindow` returns `NULL`.

### When a window is hung

A counted window is hung when `IsHungAppWindow` returns `TRUE`. That means its
thread has not retrieved messages for five seconds.

### Ghost windows

When Windows notices a hung top-level window, it covers it with a ghost window
titled "(Not Responding)". The ghost belongs to `dwm.exe`.

- `IsHungAppWindow` returns `TRUE` for the ghost.
- `HungWindowFromGhostWindow` maps the ghost back to the hung window.
- TMTADN charges the hung window's process, not `dwm.exe`, the same way Task Manager
  does.
- Both ghost functions are exported by name from `user32.dll` but are missing from the
  SDK headers. `Taskmgr.exe` imports both.

In `win32kfull.sys`, the hung query `NtUserQueryWindow` calls `ProcessHungWindow`.
The first `IsHungAppWindow` call that finds an eligible hung window therefore
also ghosts it, exactly as a query from Task Manager does. The opt-in test below
observed this. The ghost appeared during the first positive query, it
reported hung, and it mapped back to the test window.

### Suspended processes are never Not responding

A suspended process never shows Not responding, because its windows stop pumping
messages. Task Manager's `_CalcProcessStatusAndResUsage` skips the hang check for a
suspended process, and TMTADN does the same.

## Precedence

When several states hold, the row shows one. Task Manager ranks them Not
responding, then Efficiency mode, then Suspended. Combined with the rule above:

| Suspended | Hung window | Efficiency mode | Shown |
| --- | --- | --- | --- |
| no | no | no | nothing |
| no | no | yes | Efficiency mode |
| no | yes | either | Not responding |
| yes | either | no | Suspended |
| yes | either | yes | Efficiency mode |

## Group rows

With semantic grouping, a group row shows the highest status held by any member.

Task Manager does the same in `WdcApplicationsMonitor::_AtmUpdateApplicationsChildProcesses`
and `AtmViewItem::UpdateSuspendedOrEfficiencyModeStatus`:

- Any member that is not responding makes the group not responding.
- A non-zero count of suspended members makes it suspended.
- A non-zero count of efficiency-mode members makes it efficiency mode.

With "Use root process as group row" on, the group's root process takes the
group row's place and shows the group totals with no status. Its first entry,
Root, shows the root process's own usage and status, so no row carries the group
status. With "Apply to subgroups" also on, every process with child processes
follows the same pattern for its own subtree.

Parent-process grouping has no synthetic rows, so every row shows its own
process.

## Sampling and cost

Status is a dynamic column. It is sampled for the rows in the viewport, or for
every process while the sort, the search, or a live total needs every process.
The window scan runs only while Status is in the active schema, meaning visible
or referenced by a search.

Efficiency mode needs a process handle, so every sampled process is opened,
queried twice and closed on each refresh. Before this column existed, the default
columns needed no per-refresh handle.

Measured on the development machine, with 50 refreshes after warm-up:

| Work | Cost |
| --- | --- |
| Efficiency query | 1.16 us per process: 0.65 ms per refresh when all 556 processes were sampled |
| Window scan | 0.12 ms per refresh over 615 top-level windows |

## Differences from Task Manager

- **Suspended.** Task Manager tests only each thread's wait reason. TMTADN also requires
  the thread to be waiting. The wait-reason field is not cleared when a thread resumes,
  so a resumed thread that runs without waiting again would otherwise still read as
  suspended.
- **Efficiency mode.** Task Manager opens a process with `PROCESS_SET_INFORMATION` as well
  as query access, and silently skips any process it cannot open that way. A
  non-elevated Task Manager therefore never shows the leaf on an elevated process, and
  its Power throttling column shows Disabled for any process it cannot open. TMTADN
  reports the state it can read with query access, and N/A when it cannot read it.
- **Not responding.** Task Manager's Processes page keeps a window marked hung for six
  seconds after it recovers. TMTADN reports the current `IsHungAppWindow` result on
  every refresh.
- **Display.** Task Manager decides between glyph and text per status and per app type.
  TMTADN uses the one mode the user chose, and can center the glyphs.
- **Not implemented.** Task Manager's rarely shown "Waiting for user" status, whose trigger
  was not traced, and its option to hide status values.

## Verification

`ProcessStatusTests` covers:

- the precedence table;
- the window rule;
- a real `ping.exe` child that is switched between the four combinations of EcoQoS and
  idle priority, checking both the Status reader and the Power throttling value;
- the same child suspended and resumed with `NtSuspendProcess` and `NtResumeProcess`.

`SemanticProcessAggregationTests` covers group rows, and
`ProcessColumnPropertiesWindowTests` covers the display and centering options.

The hung-window test is opt-in because it shows an off-screen window, with a
taskbar button, for about seven seconds. It creates a window, stops reading its
queue, and asserts all of the following:

- The process is reported within fifteen seconds.
- The ghost that appears belongs to another process, but still charges this one.
- `dwm.exe` is never reported.
- The report clears after the queue is read again.

```powershell
$env:TASK_MANAGER_RUN_HUNG_WINDOW_TEST = '1'
dotnet test .\TaskManagerTrayAppDotNET\tests\TaskManagerTrayAppDotNET.Tests\TaskManagerTrayAppDotNET.Tests.csproj `
  --filter 'FullyQualifiedName~HungWindowMarksItsOwningProcessNotResponding'
```
