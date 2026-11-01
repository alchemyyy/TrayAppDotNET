# Elevated privileged-action broker

Companion spec for [01_elevated_broker_architecture.puml](01_elevated_broker_architecture.puml).

## Purpose and non-goals

Give tmtadn parity with the administrative actions Windows Task Manager already exposes (end task,
set priority/affinity, create dump, service start/stop/restart/disable, all-users startup
enable/disable, disconnect another session) without running the tray app elevated. A single elevated
broker performs those on the UI's behalf under one consent, replacing today's eager, kill-only
elevation.

Non-goals: no manifest change (stays `asInvoker`), no whole-app elevation, no capability beyond what
Task Manager already offers. The IFEO "Replace Task Manager" card is independent and untouched.

## Components

- **ElevationBrokerClient** (medium IL, new): a per-session named-pipe client. Action call sites use
  it only after an in-process attempt returns `ACCESS_DENIED`.
- **ElevationBroker** (elevated, new): `tmtadn.exe --elevated-broker`, dispatched at the top of
  `Program.Main` like the other mode args, never starting the UI. Performs registry/SCM/WTS/
  process-handle ops in managed code, and owns and drives the kill helper for terminations.
- **Native kill helper** (unchanged): its client moves from the UI to the broker; its launcher moves
  from a UI `runas` to a normal `CreateProcess` by the already-elevated broker. Arm/fire protocol and
  native code untouched.
- **ProcessTerminationService** (medium IL, slimmed): forwards terminate to the broker; the
  elevated-helper launch logic relocates into the broker.

## Lifecycle: lazy consent

1. App starts `asInvoker`, no elevation. The eager `StartInitialElevatedTerminationAttempt` is removed.
2. A privileged op first runs in-process at medium IL. On `ACCESS_DENIED`, the UI shows one in-app
   confirmation (the existing `PromptForManualElevatedTerminationAsync` pattern), then launches the
   broker via ShellExecute `runas`: one UAC prompt.
3. Decline leaves everything unchanged and is NOT remembered: every later admin action re-prompts
   until elevation is actually granted.
4. The broker serves the UI's one connection; while it stays up, every later op reuses it with no
   prompt. It exits when that connection drops (a client-side timeout drops it too) or the UI exits
   (parent-death watch on a handle it holds), and the next admin action prompts and launches a new one.

## IPC

Named pipe, current-user ACL. The broker is the server (it owns the ACL and authenticates the client);
the UI is the client and connects with a short retry until the broker's pipe is up. Request/response,
one enumerated op per request, correlation id, typed result (`Success` / `AccessDenied` /
`InvalidTarget` / `NotFound` / `Failed` + message). The client uses asynchronous reads and writes with a
per-operation timeout (15 s for handle and registry ops, 90 s for service control, 10 minutes for a
dump) and drops the connection on timeout, so a slow or hung broker never blocks the UI thread. A line
the broker cannot parse still gets a `Failed` answer. The op set is closed:

- `TerminateProcess(pid, exitCode)`
- `SetPriority(pid, class)`
- `SetAffinity(pid, mask)`
- `CreateDump(pid, fileHandle)`: the handle value of an empty file the UI created
- `ServiceControl(name, verb)`
- `SetStartupApproval(entry, hive, enabled)`
- `DisconnectSession(sessionId)`

There is no "run an arbitrary executable" op.

## Kill-path integration (deferred)

Process termination still runs on its existing path (`ProcessTerminationService` + the native kill
helper), not through the broker. The kill helper makes no eager startup elevation attempt, so there is
no eager UAC prompt; Always run as administrator (`ElevatedStartTask`, a per-account scheduled task)
starts the whole app elevated instead. Routing terminate through the broker - having the broker spawn
and drive the kill helper - is the deferred next step, because it re-architects the critical kill path
and needs runtime verification.

## Security model

- Pipe ACL restricted to the current user.
- The broker serves only the UI that launched it. The command line carries the UI's PID
  (`--broker-parent`) and process start time (`--broker-parent-start`); the broker opens that process,
  requires the start time to match, and keeps the handle open for its whole life, so the PID cannot be
  recycled. A connecting client must have exactly that PID (`GetNamedPipeClientProcessId`). Matching the
  image path is not enough: a same-user process could start a suspended second copy of the exe, so
  the image check was replaced. A same-user process that reaches the pipe first is disconnected and
  never served. Same-user code that can inject into or duplicate handles from the UI process itself is
  outside what a same-user broker can defend against (Windows does not treat UAC as a boundary).
- One connection only: after the UI's connection drops, the broker exits instead of listening again.
- Closed op set with elevated-side input validation: startup approval writes only accept the three
  `Explorer\StartupApproved` subkeys (`Run`, `Run32`, `StartupFolder`) with a non-empty value name;
  priorities, verbs and states must be defined values. Every served op is logged with its target, the
  client PID and the result.
- No elevated file writes into user folders: the broker writes no log file and takes no settings folder.
  Its log lines travel to the UI ahead of each response (`log|` lines, line breaks escaped) and the UI
  writes them into its own log; a same-user process controls that folder and could otherwise redirect an
  elevated append or rotation with a junction. The elevated taskmgr.exe redirect change reports only
  through its exit code.
- The kill helper refuses a target without a creation time, so it never acts on a PID it cannot tell
  apart from a recycled one. An elevated Task Manager starts the standard kill helper directly so it
  inherits the elevated token (a helper started through Explorer would run at medium integrity and
  could not duplicate handles from the elevated parent).
- `dbghelp.dll`, used by the dump op, is loaded from System32 only.
- The broker never creates the dump file. The UI creates it in its own `%TEMP%\TaskManagerTrayAppDotNET`
  (CreateNew, as the medium-integrity user, so a junction there gains nothing) and sends only the handle
  value. The broker opens the pinned UI process with `PROCESS_DUP_HANDLE`, duplicates that handle, requires
  a disk file that is a regular, non-directory, non-reparse-point file with a link and zero length, and
  writes the dump into it. The UI reports the path; on failure it deletes the file, and after a timeout it
  keeps the file because the broker may still be writing.

## Build and AOT

The broker is a new `Program.Main` mode arg, no new binary. Under NativeAOT it is the same AOT exe
re-run with `--elevated-broker`, exactly like `--kill-helper`. Because new op types are managed
handlers in the broker, there is no native protocol change and no C++/AOT rebuild churn as the op set
grows.

## Status (2026-09-29)

Wired through the broker (lazy per-action consent): `SetPriority`, `SetAffinity`, `CreateDump`,
`ServiceControl`, `SetStartupApproval`, `DisconnectSession`. The UAC shield shows on the
priority/affinity context-menu entries when the target needs elevation.

One setting, default off: `BypassElevationBrokerWhenElevated` lets an already-elevated app perform admin
actions in-process instead of through the broker; off keeps behavior consistent through the broker.

Deferred: unifying terminate under the broker (routing termination through it and spawning the kill
helper from it). Everything already wired is managed handlers plus call-site routing on
`ACCESS_DENIED` (or the deterministic all-users startup check), no native work.

## Resolved and open

- Decline memory: resolved - a decline is never remembered; every ungranted admin action re-prompts.
- Elevated bypass: resolved - `BypassElevationBrokerWhenElevated` (default off) supplies the option
  while keeping the broker path as the consistent default.
- Open: unifying terminate under the broker. Broker lifetime is one UI connection (see Lifecycle).
