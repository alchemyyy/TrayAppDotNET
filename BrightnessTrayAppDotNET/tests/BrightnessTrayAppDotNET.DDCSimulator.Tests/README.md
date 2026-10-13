# btadn DDC simulator

Runs the real `MonitorService` and `DDCRecoveryService` against a simulated machine: monitors, GPU connectors, the
single DDC/CI path they share, the Windows display stack, and btadn's per-monitor helper processes. Everything runs on
virtual time, so an hour of DDC traffic finishes in seconds and a ten-hour sleep costs nothing.

## Running

```powershell
# Everything: simulator self-tests, named scenarios, default sweeps
dotnet test BrightnessTrayAppDotNET/tests/BrightnessTrayAppDotNET.DDCSimulator.Tests -c Debug -p:Platform=x64

# Named scenarios and self-tests only (a few seconds)
dotnet test ... --filter "Category!=Sweep"

# Large sweeps
$env:DDCSIM_SEEDS = 2000; $env:DDCSIM_MATRIX = "full"; dotnet test ... --filter "Category=Sweep"

# Replay one generated scenario; DDCSIM_TRACE=1 writes its trace even when it passes
$env:DDCSIM_SEED = 23; $env:DDCSIM_TRACE = 1; dotnet test ... --filter "FullyQualifiedName~RandomScenarioSweep"
```

Reports, sweep summaries and full traces land in `%TEMP%\BrightnessTrayAppDotNET.DDCSimulator.Tests\<pid>`. A trace
interleaves simulator events, bus failures, helper launches and btadn's own `TADNLog` lines on the virtual clock. The
module initializer points settings, `displays.json` and logs at that folder, so no test touches the installed app.

## How time works

`SimulationClock` is a `TimeProvider`. `MonitorService`, `DDCRecoveryService` and `AsyncThrottler` take it through
their internal constructors; production passes the system clock and keeps `Thread.Sleep`. Timers fire only when the
clock advances, and the clock advances only when every participant is parked in a simulator wait, idle, or blocked.
Blocked versus computing is decided from CPU cycle counters (`QueryProcessCycleTime` minus the driver thread), so JIT
or real work holds time still while a thread waiting on a `SemaphoreSlim` does not. A 30 s watchdog turns a deadlock
into a `SimulationHangException` with a state dump, and each scenario has a 20 s real-time budget.

The simulated UI thread (`SimulationDispatcher`) carries a `SynchronizationContext`, so `await` continuations in
`MonitorService` resume on it exactly as on Avalonia's UI thread.

## Model and sources

| Piece | Behavior | Source |
| --- | --- | --- |
| DDC/CI framing | 0x6E/0x51 host frames, 0x6E/0x50 reply checksums, Get/Set VCP, capabilities fragments | VESA DDC/CI 1.1 |
| Host waits | 40 ms Get reply minimum, 50 ms post-Set, 50 ms per capabilities fragment | VESA DDC/CI 1.1 |
| Windows wait | 50 ms per request/reply inside win32k (`-500000 x 100 ns`) | disassembly, build 26200 |
| Shared path | One global mutex across every monitor for the whole call, including the wait | win32k `CAutoMutexOPM` |
| Bus speed | 100 kHz I2C, 9 clocks per byte; DP and adapters 4x for I2C-over-AUX | I2C standard mode |
| Error codes | NAK 0xC0262582, receive 0x83, unsupported 0x84, invalid data 0x85, no command 0x89, length 0x8A, checksum 0x8B | winerror.h, recorder logs |
| LG ULTRAGEAR+ | ~2 s MCU boot, 25-35 % garbled replies for 26-30 s after power-on, 0.6-1.5 s of garbage after DDC hard-off, an early read after wake can wedge the reply pipeline into checksum failures (0x8B) until a SET | hdmi-relink recorder, btadn comments |
| Profile variants | Clean (no wedge, no busy NAK), slow replies (25-85 ms preparation, so late replies read back as null messages, 0x89), write-only, no DDC, lossy (dropped SETs, stale replies) | DDC/CI null-message rule, varied by the sweeps |
| Links | NAK in 3-10 ms direct, 30-40 ms through adapters; adapters answer garbage (0x89) and accept SETs while the panel is off | recorder logs |
| Brightness persistence | A value is saved 3 s after it last changed; a power cycle restores the saved value. A panel whose MCU answers while soft-off keeps saving then. Some panels apply a SET 100-900 ms late | assumption, varied by the sweeps |
| Hot-plug | RTX 5080: power cycles invisible to Windows. RTX 4080: DP removed at once, HDMI after 22-33 s | recorder logs |
| Event order | Removal: REMOVE_COMPLETE, path gone +151 ms, WM_DISPLAYCHANGE +209 ms. Arrival: path +424 ms, ARRIVAL +438 ms, WM_DISPLAYCHANGE +514 ms | recorder logs |
| Transient | One display active for 4.3 s 1.9 s after an arrival; inactive monitors listed under every source, sometimes first | recorder logs |
| Display numbers | Source-bound, connector-bound or compact; first-free source allocation; climbing GDI names | assumption, varied by the sweeps |
| EDID registry lag, Default_Monitor devnodes, device attach lag, CCD failures | Configurable, varied by the sweeps | assumption |

## What runs for real and what is mirrored

Real code: `MonitorService`, `DDCRecoveryService`, `AsyncThrottler`, `KnownDisplaysStore`, `EDIDParser`,
`DDCMonitorDatabase`, `CCD.ResolveFriendlyDisplayNumber`, the capabilities parser, and the decisions
`DDCHelperServer.FindMatchingMonitor`, `DisplayService.SelectRefreshHandleCandidate`, `SelectMonitorDeviceIndex`,
`SelectStaleHelperClientKeys`, `BuildHelperClientKey`, `DisplayEventManager.AreProfileMonitorsLoaded` and
`HasMonitorGap` over `MonitorService.CaptureMonitorPresence`.

Mirrored, and to be kept in step with the production code:

- `SimulatedDisplayService` repeats `DisplayService.TryGetMonitorsCore` (the active monitor device of each source, read
  twice, adapter-name fallback), the retirement of helpers whose identity stopped enumerating or was superseded,
  `DDCHelperClient.SendCommand` (serial slot, lazy spawn within the pipe budget, end-to-end timeout that kills the
  helper, retirement once the running command finishes, a helper that ends with the command for a retired identity
  or for one a retired helper refused, error strings), and `DisplayService`'s bus gate, which lets one command at a
  time onto the bus across every helper and keeps the queueing out of the timeout.
- `AppEventBridge` repeats DisplayEventManager's Win32 plumbing: 250 ms UI debounce, 1 s x 10 burst, flyout scan with
  its recovery request.
- `CurveDriver` repeats the absolute-mode parts of EnvironmentalCurveService that touch `MonitorService`.
- `SimulationWorld.SetBrightness` releases a curve-driven row first, as grabbing its slider in the flyout does.

## Oracle

Checked whenever btadn publishes rows: no physical panel shown twice (`DuplicateRow`), no shared row IDs
(`DuplicateRowID`), every helper command reaching the panel its DDCMonitor described (`MisroutedCommand`), no brightness
SET reaching a panel after the SET of an app power-off did while its row still holds that intent
(`WriteToPoweredOffPanel`; a SET already in flight when the intent was published lands first and is allowed, and a
Ctrl+click hard-off publishes no intent), no exception escaping to the UI thread, a timer, or an app catch-all log line.

Checked once panels have been enumerated and DDC-healthy for the 20 s grace: each has exactly one row (`MissingRow`,
`StaleRow`), its DDC entry drives that panel (`MisboundEntry`), and the panel shows the target that owns it
(`BrightnessNotConverged`). The row must also be working (`StuckUnavailable`) once the grace plus 35 s has passed, which
covers `DDCRecoveryService` backing a long-failing row off to one probe every 30 s. A slow-reply panel counts as healthy
because it answers part of its reads, so a row that stays Failed means btadn stopped retrying. With one read per
attempt (`ValidationAttempts` 1) such a panel fails most single reads, including each replay's verification, so its
row may cycle between recovered and unavailable; for that pairing the check only requires that btadn sent the panel a
command within the longest backoff. A row may keep an app
power-off intent after the panel's own button woke it, but not after the app was asked to power it on
(`StuckUnavailable`). A wedged LG must be resynchronized when btadn holds a confirmed bus value, unless the app was last
asked to keep it off (`WedgeNeverResynchronized`), and live helper processes may not outnumber panels
(`HelperProcessLeak`). Reports also note helper churn above 120 launches per awake hour.

## Gaps found and fixed

The first sweeps (2026-10-12) reported findings in 112 of 300 random seeds and 21 of 384 matrix cells. After the fixes
below, 300 random seeds, all 384 matrix cells and 2,000 further seeds (1000-2999) report none. The named scenarios and
the seeds listed here are the regression cover; replay a random seed with `DDCSIM_SEED`. Mark a test
`[Trait("Category", "Gap")]` while it reproduces an unfixed gap.

| Finding | Cause | Fix | Scenarios and seeds |
| --- | --- | --- | --- |
| `DuplicateRow` (the screenshot) | `FindRecoveryTarget` matched a failed row's sticky `num:N` ID against whichever panel Windows now numbers N, and `PromoteRecovered` copied that panel's identity onto the row. | An `edid:` key is matched first, and no weaker rule may pick a panel whose EDID serial contradicts the row or that another row's entry drives. | `DisplayNumberShiftDuringRecovery...`, `SwappedDisplayNumbersThenChecksumWedge...`; 23, 42, 178, 281, 285 |
| `DuplicateRowID`, `MisboundEntry` | The new-row path assigned `num:<current number>` without checking live rows, and `_entries[id] = ...` took over the entry the older row used. `TryRecoverMonitor` resolved a shared ID to the healthy twin, so the failed one was never probed. | `ResolveUniqueMonitorID` falls back to the EDIDKey, then the port form, for new, restored and re-keyed rows; recovery prefers the row that needs it. | 13, 23, 101, 149 |
| `StuckUnavailable` | A row that never acquired DDC was retried only when `displays.json` already marked it capable: a panel dark at launch, a slow-reply panel losing its launch probe, or a capability recorded under a `port:` key during EDID lag. | Every unavailable row is a recovery candidate with per-candidate backoff (2, 2, 2, 4, 8, 16, then 30 s), and opening the flyout makes them due at once. | `PanelOffAtFirstLaunch...`; 2, 26, 29, 53, 69, 133, 141, 154, 165, 180, 216, 293 |
| `WedgeNeverResynchronized` | `DemoteOnDDCFailure` left `IsReadDegraded` set, and the read-degraded branch of `TryRecoverMonitor` never sent the checksum-resynchronizing SET. A refused power-on also restored the earlier off intent, which kept recovery away. | Demotion clears `IsReadDegraded`; a read-degraded row with checksum read errors gets the resync SET; a refused power-on clears the suppression. | `AdapterPanelWedgedOnReturn...`; matrix cells 10330, 10346 |
| `WriteToPoweredOffPanel` | `SetPowerStateAsync` published the power-off intent only after the 0xD6 write was accepted, so a curve tick in between landed a brightness SET on the dark panel. | The intent is published under the brightness target gate before the write, and target preparation rechecks it under that gate. | 153, 285; cells 10306, 10346 |
| `MisroutedCommand` | `FindMatchingMonitor` and `RefreshHandle` took the first monitor matching any rule, so a stale GDI name reached the panel that now owns it. DeviceID and instance path came from `EnumDisplayDevices` index 0, an inactive monitor during transients and attach lag. | The strongest rule wins across all monitors. A helper name match must be the HMONITOR the parent enumerated and is refused when the EDID serial or a stable DeviceID contradicts it; an adapter-name DeviceID counts as a name. Only an active monitor device describes a source. | 42, 237, 1506, 2000 |
| Helper churn | Every failed native read retired the helper, so a dark panel relaunched about 4,800 helpers an hour; accepted SETs to a dark adapter panel restarted the streak. | Retirement once a streak reaches three failures in a row, then once per 5 minutes; only a successful read ends a streak; a read-degraded row resends an unconfirmed value at most once a minute. | `PanelOffForAnHourDoesNotChurnHelperProcesses` (was about 4,800 launches in the hour) |
| `HelperProcessLeak` | Helper clients of identities that disappeared were never dropped. | Parent enumerations retire helpers of vanished identities; a busy one ends when its command finishes. | `CableMovedToAnotherConnector...`; 6, 9, 10, 63 |
| Refresh storm (trace) | The burst scanner compared model IDs read out of row IDs; `num:` and `edid:` IDs carry none, so every tick refreshed and restarted the 1.5 s settle. | Exact devnode instance paths from Device Manager are compared with the panels behind live rows. | 69 |
| Attach-lag identities, `HelperProcessLeak` | Rows minted while Windows had no active monitor device for their source carry the GDI name as DeviceID and a `port:\\.\DISPLAYn` key. The startup sweep skipped because those panels answered DDC; the first later Refresh found no row for the real identities, detached the rows and recreated them without their state. A power verification still running on a detached entry then registered a helper for the vanished identity. | Adapter-name rows keep the startup sweep running, and Refresh re-keys them in place when their source shows a panel no other row describes. Detaching a row stops its power verification, and a command for a retired helper identity runs on a helper that ends with it. | 1137, 2220 |
| Demote/recover flapping (trace) | A panel that answers reads but does not hold a SET (late apply, ignored writes) failed the replay's read-back right after every recovery, and the demotion's recovery request made it due at once: about four cycles a second. | A row that fails again within 10 s of recovering waits one more backoff step per relapse in a row. | `PanelThatIgnoresSetsBacksOffInsteadOfFlapping`; 1137 (252 s), 2189 |
| `WedgeNeverResynchronized` | A power-on request for a Failed row, or a read-degraded row with blind writes off, returned before clearing the app's earlier power-off intent, so recovery skipped the woken, wedged panel for the rest of the session. | Such a request lifts the intent and requests recovery; a powered-off row that enumerates again drops it too. | 1399 |
| `BrightnessNotConverged` | Two causes. The simulator never saved a value a soft-off panel with a live MCU accepted, and applied late SETs as of the moment it next looked, so a button power-on restored an old value. In btadn, a refused power-on, here racing the panel's own button, skipped the re-probe and replay an accepted one runs. | Simulator: the save is evaluated at the power-on, from the SET's real apply time. btadn: a refused power-on re-probes and replays through the same settled Refresh. | `RefusedPowerOnRacingThePanelButtonReplaysTheTarget`; 2189 |
| `WriteToPoweredOffPanel` (oracle) | A Ctrl+click hard-off armed the check without publishing a row intent, so a curve SET that passed its generation check before the app's own power-off was flagged. | The check arms only on the off SET of an intent a row holds. | matrix cell 10010 |
| Slider drag stutter (user report) | Each write was read back 50 ms after it landed, so a drag slower than the bus read back every step, and a read costs as much shared bus time as a write (GET 63-131 ms, SET 57-117 ms on the user's LGs). Every failed read also relaunched the helper, which stalled that panel's next write 194-242 ms. | A write is read back only once its target has stayed current for the verification delay (500 ms by default); the helper is retired only after three failures in a row, and an unreadable read-back waits the fixed read backoff (80, 160, 480 ms) before the next. | `SlowDragReadsBackOnlyWhereItStops` (36 read-backs during the drag, now 0); 1616 |
| `StuckUnavailable` | A row that dropped off the bus while read-degraded was parked as Failed with `IsReadDegraded` still set. Restored, it sent every recovery pass down the read-only degraded branch, which never probes the write transport of a row without an entry. | Parking a row clears `IsReadDegraded`. | 69 |
| Rows winking out (user report) | With `ValidationAttempts` 1, a panel still applying a value read back the old one, its row was demoted, and recovery read it back a moment later. The verification delay setting could only shorten the first read-back wait (capped at 300 ms, 50 ms for power), so raising it for a slow panel did nothing. | Every read-back waits the full verification delay, brightness and power, the first one and the one after each re-sent value; an unreadable reply is read again on the fixed read backoff. A demoted row's reason says what the writes or read-backs showed and how many attempts were made. | `LateApplyingPanelIsConfirmedAfterTheVerificationDelay`, `DarkPanelBehindAdapterIsMarkedUnavailable` |

## Known limits

No sweep reports these; they are bounds of what btadn can observe or of the settings a scenario runs under.

| Limit | What happens |
| --- | --- |
| Invisible button power cycles | A panel that comes back from its own button with a different saved brightness, such as one switched off less than 3 s after a SET, is invisible on the RTX 5080. btadn corrects it at its next write, recovery, Refresh or app power-on; it does not poll brightness. |
| One read per attempt | With `ValidationAttempts` 1, a panel that misses most single reads cannot pass a replay's verification for long, so its row cycles between recovered and unavailable at the relapse backoff instead of settling. The oracle only requires that btadn keeps probing it. |
| Adapter-name keys in `displays.json` | Capability recorded under a `port:\\.\DISPLAYn` key during attach lag stays in the store after the row is re-keyed, so later launches run both startup sweep Refreshes. |
