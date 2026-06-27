# Monitor recovery candidate publication race

## Observed failure

`TargetedRecoveryMatchesPortFormWhenDisplayNumberDriftsAndSerialIsMissing` intermittently failed at the first
candidate assertion. The monitor was already `Failed`, but `GetStuckRecoveryCandidateIDs()` returned an empty list
instead of `num:3`. The same binary passed immediately when the test was rerun in isolation.

Test parallelization is disabled for this assembly, so this was not shared state from another test.

## Interleaving

`MonitorService.RefreshProbePhaseAsync` performed initial acquisition asynchronously:

1. The DDC brightness read succeeded.
2. A functional `MonitorInfo` was created and published with `Monitors.Add(info)`.
3. Only after publication, terminal refresh bookkeeping called `RecordDDCCapableObservations()` and
   `ProjectWasEverDDCCapableToMonitors()`.

The test's inline dispatcher has no single-threaded synchronization context. Consequently, the test could observe
step 2 while the first refresh continuation had not reached step 3:

1. The test observed `Monitors.Count == 1 && monitor.IsHardwareFunctional`.
2. It changed the fake DDC read to fail and started a second `Refresh()`.
3. The second probe demoted the monitor to `Failed`.
4. `RecordDDCCapableObservations()` could now skip the row because the second refresh had already made it failed.
5. `ProjectWasEverDDCCapableToMonitors()` copied the still-false persisted value onto the runtime model.
6. The candidate was excluded because `WasEverDDCCapable` was false in both places.

Usually the first continuation completed step 3 before the second probe ran, which is why the failure depended on
thread-pool scheduling.

## Invariant and correction

A successful hardware probe is itself the authoritative evidence that the monitor was DDC-capable. The runtime
`MonitorInfo.WasEverDDCCapable` bit and the in-memory known-display record must therefore be set at that successful
probe, before either:

- publishing a new row through `Monitors.Add`, or
- transitioning an existing row back to a hardware-functional state.

The property is monotonic: once true, later projection from `displays.json` may not reset it to false. Candidate
selection can use the immediately published runtime bit and no longer depends on terminal refresh bookkeeping.
The regression test now asserts that a newly published functional row already carries the sticky capability bit.
