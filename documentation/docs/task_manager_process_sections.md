# Task Manager process sections

With semantic grouping, the TMTADN Processes page lists its groups in the
sections of the Windows 11 Task Manager Processes page: Apps and Background
processes, plus Windows processes when **Group Windows processes** is on. Task
Manager's rules on this page were read from the installed `Taskmgr.exe`
10.0.26100.9278 with Microsoft's public symbols. The places where TMTADN
deliberately differs are listed at the end.

[Section rule](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/SemanticProcessTreeBuilder.cs)
[Windows process rule](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/SemanticProcessInfrastructurePolicy.cs)
[Image path without a handle](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessImagePathResolver.cs)
[Window scan](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessWindowGroupingFactsCollector.cs)
[Tests](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests/SemanticProcessTreeBuilderTests.cs)

## Task Manager's rule

`WdcApplicationsMonitor::SetProcessItemGroup` puts each process in one group. It
checks these in order and stops at the first match:

1. Explorer, `%windir%\explorer.exe`: Apps while it has a window, Windows
   processes otherwise.
2. Task Manager itself: Apps.
3. A process the window monitor has just flagged for Apps: Apps.
4. The file picker host, `PickerHost.exe`: Background processes.
5. The legacy Windows Sidebar: Apps.
6. A critical process: Windows processes.
7. A background task broker, `backgroundTaskHost.exe` or
   `backgroundTransferHost.exe`: Background processes.
8. An immersive broker other than Task Manager: Background processes.
9. A process with a window: Apps.
10. A process whose app ID has a window in another process: Apps.
11. Anything else: Background processes.

`WdcApplicationsMonitor::IsCriticalProcess` decides step 6. A process is
critical when any of these holds:

- Its process ID is 0, 1 or 4, the IDs `HasDisabledOperations` accepts.
- The kernel classifies it as the System, Secure System, Memory Compression or
  Registry process. This is the classification field of
  `SYSTEM_PROCESS_INFORMATION_EXTENSION`, values 1 to 4.
- Its image path is in the `TmSpecialProcesses::CriticalProcessPaths` table.
  Task Manager expands the environment variables and compares case-insensitively.
  - `%windir%\system32\`: `winlogon.exe`, `wininit.exe`, `csrss.exe`,
    `lsass.exe`, `smss.exe`, `services.exe`, `taskeng.exe`, `taskhost.exe`,
    `dwm.exe`, `conhost.exe`, `svchost.exe`, `sihost.exe`
  - `%programfiles%\Windows Defender\`: `msmpeng.exe`, `nissrv.exe`
- `ProcessBreakOnTermination` is set, the flag `RtlSetProcessIsCritical` sets.
  Task Manager skips this check for immersive processes. It reads the flag
  through a `PROCESS_QUERY_INFORMATION` handle, which a protected process
  refuses, so the path table is what catches the protected system processes.

Task Manager nests some child processes under an application row.
`_SetAggregatorItemGroup` gives that row the best group of its members: Apps,
then Background processes, then Windows processes.

## TMTADN's rule

TMTADN classifies each semantic group:

1. A group with any member that has an app window is listed under Apps. This
   includes Windows processes, so Explorer is an app while a File Explorer window
   is open.
2. Otherwise the process that represents the group decides. The group is listed
   under Windows processes when that process is a Windows process, and under
   Background processes otherwise.

A Windows process is one that Task Manager's `IsCriticalProcess` would call
critical, plus Explorer:

- process ID 0 or 4;
- the System, Secure System, Memory Compression, Registry and System Idle
  Process pseudo processes;
- `ProcessBreakOnTermination` set;
- an image path in Task Manager's table, or `%windir%\explorer.exe`.

An app window is a top-level window that is visible, unowned, titled, not a tool
window and not cloaked. The desktop window, Progman, is a tool window, and the
taskbar has no title, so Explorer counts as an app only while it shows a File
Explorer window.

A process's own section and its group's section can differ. A console host,
`conhost.exe`, is a Windows process, but it groups beneath the console program it
serves and follows that program's section.

### Ghost windows

When a window hangs, Windows covers it with a ghost window that belongs to
`dwm.exe`. The window scan charges the ghost to the hung window's process, as the
Status column does, so `dwm.exe` never moves to Apps while another program hangs.

### Processes that cannot be opened

With a basic-user token on the development machine, 127 of 402 processes could
not be opened even with `PROCESS_QUERY_LIMITED_INFORMATION`, including 69
`svchost.exe` instances. Task Manager handles this in
`TmGetPathFromProcessIdForNonAdmin`: it reads the image path by process ID from
`NtQuerySystemInformation(SystemProcessIdInformation)` and maps the native device
path back to a drive letter. TMTADN does the same for semantic grouping, so the
path table applies whether or not TMTADN is elevated. The Image path column, the
icon and the description still come from the process handle.

The drive mapping reads `QueryDosDevice` for the letters A to Z. That is an
object-namespace lookup that finished in under a millisecond for all 26 letters
on the development machine, including disconnected network drives.

## Setting

**Settings > Processes > Grouping > Group Windows processes** is off by default.
Windows processes are then listed under Background processes, or under Apps when
they have an app window. Turning it on adds the Windows processes section, as
Task Manager has. The option appears only while the grouping style is Semantic
application.

## Measured on the development machine

An elevated snapshot of 171 groups, with Group Windows processes on:

| Section | Groups | Contents |
| --- | --- | --- |
| Apps | 7 | Brave, Visual Studio, Discord, Explorer with File Explorer windows open, Sublime Text, TMTADN, Zed |
| Background processes | 70 | services, tray apps and helpers, including `taskhostw.exe`, `fontdrvhost.exe` and Defender |
| Windows processes | 94 | 76 `svchost.exe`, 3 standalone `conhost.exe`, `csrss.exe` x2, `dwm.exe`, `lsass.exe`, `LsaIso.exe`, `services.exe`, `sihost.exe`, `smss.exe`, `wininit.exe`, `winlogon.exe`, and the System, Registry, Memory Compression, Secure System and System Idle Process pseudo processes |

Before this rule, TMTADN listed a group under Windows processes when any member
was a pseudo process, Explorer, one of nine core system images, or a process
marked critical or protected. Most `svchost.exe` instances, `sihost.exe` and
standalone console hosts landed in Background processes. Explorer stayed under
Windows processes even with a window open, and protected services such as the
Defender engine were listed there too.

## Differences from Task Manager

- **App windows.** Task Manager moves only Explorer to Apps when it has a
  window. TMTADN does this for every process.
- **Group sections.** A TMTADN group follows its representative process, not the
  best group of its members. TMTADN can group headless helpers beneath a service
  host, which Task Manager would list as separate Background rows; they stay with
  their service host under Windows processes.
- **Critical flag.** TMTADN reads `ProcessBreakOnTermination` through the handle
  its sampler already holds, which can be a limited-query handle, so it also sees
  the flag on protected processes. On the development machine, every protected
  critical process is also in the path table, so the sections agree.
- **Defender.** The table names `%ProgramFiles%\Windows Defender\MsMpEng.exe` and
  `NisSrv.exe`. Defender platform updates run from
  `%ProgramData%\Microsoft\Windows Defender\Platform\<version>`, so neither Task
  Manager nor TMTADN lists them under Windows processes there.
- **Not implemented.** The immersive-broker and background-task-broker exceptions,
  which need the process UI context and only matter for brokers with windows; the
  file picker, Sidebar and self exceptions; the window monitor's transient flag;
  and a feature-gated exception for the Edge PWA helper, `pwahelper.exe`.

## Verification

`SemanticProcessTreeBuilderTests` covers:

- Explorer with and without an app window;
- the path table, including Defender under Program Files;
- a system image name outside the system directory;
- the pseudo processes;
- the critical flag, and protection alone, which fences a process without making
  it a Windows process;
- a Windows process with an app window;
- a service host with a headless helper;
- a console host beneath a headless and a windowed program.

`ProcessImagePathResolverTests` covers the device-to-drive mapping and reads the
paths of the test process and of `smss.exe` without a handle.
`ProcessDetailsCanvasSemanticGroupTests` checks the sections with the option on
and off, and `AppSettingsTests` checks its default and persistence.
