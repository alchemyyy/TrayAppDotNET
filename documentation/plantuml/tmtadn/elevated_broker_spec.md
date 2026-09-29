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
4. The broker persists for the session; once granted, every later op reuses it with no prompt. It
   tears down on UI exit (parent-death watch, the mechanism the kill helper already uses).

## IPC

Named pipe, current-user ACL (the pattern already used by the activation server and the installer
progress pipe). The broker is the server (it owns the ACL and authenticates clients); the UI is the
client and connects with a short retry until the broker's pipe is up. Request/response, one enumerated
op per request, correlation id, typed result (`Success` / `AccessDenied` / `InvalidTarget` /
`NotFound` / `Failed` + message). The op set is closed:

- `TerminateProcess(pid, exitCode)`
- `SetPriority(pid, class)`
- `SetAffinity(pid, mask)`
- `CreateDump(pid, kind)`
- `ServiceControl(name, verb)`
- `SetStartupApproval(entry, hive, enabled)`
- `DisconnectSession(sessionId)`

There is no "run an arbitrary executable" op.

## Kill-path integration (deferred)

Process termination still runs on its existing path (`ProcessTerminationService` + the native kill
helper), not through the broker. The kill helper's eager startup elevation is now gated by the
`EnableElevatedTerminationOnStartup` setting (default off), so there is no eager UAC prompt. Routing
terminate through the broker - having the broker spawn and drive the kill helper - is the deferred
next step, because it re-architects the critical kill path and needs runtime verification.

## Security model

- Pipe ACL restricted to the current user.
- The broker authenticates the connecting client before serving any request: it resolves the client
  PID with `GetNamedPipeClientProcessId` and requires the client's image path to equal the broker's
  own installed executable path. Placing a copy at that path already requires admin, so combined with
  the current-user ACL this blocks a hostile same-user process from driving the broker. Authenticode
  publisher verification is a hardening layer to add for signed release builds.
- Closed op set; each op validates its target and is written to the Task Manager log for auditability.
- A kill-capable elevated helper already exists today, so this is not a new class of exposure, but it
  raises the value of the surface, which is why client authentication is added now rather than deferred.

## Build and AOT

The broker is a new `Program.Main` mode arg, no new binary. Under NativeAOT it is the same AOT exe
re-run with `--elevated-broker`, exactly like `--kill-helper`. Because new op types are managed
handlers in the broker, there is no native protocol change and no C++/AOT rebuild churn as the op set
grows.

## Status (2026-09-29)

Wired through the broker (lazy per-action consent): `SetPriority`, `SetAffinity`, `CreateDump`,
`ServiceControl`, `SetStartupApproval`, `DisconnectSession`. The UAC shield shows on the
priority/affinity context-menu entries when the target needs elevation.

Two settings, both default off:
- `EnableElevatedTerminationOnStartup` gates the kill helper's eager startup UAC, so by default there
  is no eager prompt.
- `BypassElevationBrokerWhenElevated` lets an already-elevated app perform admin actions in-process
  instead of through the broker; off keeps behavior consistent through the broker.

Deferred: unifying terminate under the broker (routing termination through it and spawning the kill
helper from it). Everything already wired is managed handlers plus call-site routing on
`ACCESS_DENIED` (or the deterministic all-users startup check), no native work.

## Resolved and open

- Decline memory: resolved - a decline is never remembered; every ungranted admin action re-prompts.
- Elevated bypass: resolved - `BypassElevationBrokerWhenElevated` (default off) supplies the option
  while keeping the broker path as the consistent default.
- Open: unifying terminate under the broker, and broker idle lifetime (currently tied to the UI
  process, no idle timeout).
