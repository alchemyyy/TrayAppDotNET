# Task Manager process disk and network data

The TMTADN Processes grid obtains per-process Disk and Network rates through
different Windows data paths. Neither path starts a kernel ETW session. Disk
piggybacks on the process snapshot already required by the grid, while Network
uses the private SRUM real-time API used by Windows Task Manager.

[Process snapshot service](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessSnapshotService.cs)
[Process table](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/UI/ProcessDetailsCanvas.cs)

## What makes a column active

The process sampler receives an active data schema from the grid. A column is
in that schema when either of these is true:

- The column is visible in the grid.
- The active search expression references the column, even if the column is
  hidden.

Consequently, a hidden `{Network}>10 Mbps` search still requires Network data.
This is intentional: removing the data source would make the expression
silently evaluate against stale or unavailable values.

The grid normally performs dynamic per-row calculations for its warm viewport
rows. Operations that need a complete ordering or filter can request dynamic
samples for every process. This reduces UI-side work, but it does not change
which records an upstream Windows provider produces.

[Active schema](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Models/ProcessDataSchema.cs)
[Warm-process policy](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessSnapshotService.cs)

## Current activation and cost behavior

| Component | Activation | Deactivation | Work while its column is absent |
| --- | --- | --- | --- |
| Base process snapshot | Starts with the process sampler | Stops when the process sampler is disposed | Continues because PID, CPU, memory, thread, and other grid data require it |
| Disk counter extraction | Parsed from every base process snapshot | Stops when the process sampler is disposed | Two 64-bit reads and one saturating addition per process still occur; Disk rate history and formatting do not |
| Network SRUM collector | Created when Network enters the active schema | Unregistered and disposed when Network leaves the active schema | No worker, COM registration, callback, dictionary, or grid-side rate calculation remains active |

Network follows the strict invariant **no active column means no active
column-specific system**. Removing Network from the grid unregisters SRUM and
stops its worker unless an active search expression still requires Network.
Adding Network again creates a fresh worker and registration.

Disk is a smaller exception to the strict invariant:

- Disk has no independent collector to turn off, but its small extension-read
  cost remains even when Disk is absent.

To enforce the same strict invariant for Disk in the future, pass a
Disk-enabled flag into the base snapshot parser and skip the disk extension
reads when Disk is absent from the active schema.

Network teardown trades complete hidden-state cost neutrality for registration
churn and a collection delay after reactivation. A new registration must
receive one callback to establish a baseline and another callback before it can
report a rate.

## Disk

### Data path

The process sampler already calls
`NtQuerySystemInformation(SystemProcessInformation)` once per refresh. On the
supported Windows versions, each x64 process entry appends private process disk
counters after its thread array:

```text
SYSTEM_PROCESS_INFORMATION
SYSTEM_THREAD_INFORMATION[NumberOfThreads]
PROCESS_DISK_COUNTERS
```

The extension begins at the process header size plus the thread count times the
thread-entry size. Its first two 64-bit values are cumulative bytes read from
disk and bytes written to disk. TMTADN bounds-checks the extension, reads those
values, and stores their saturating sum.

These counters are different from the ordinary process I/O transfer counters.
The ordinary counters also include non-disk file, pipe, device, and network I/O
and therefore cannot implement a true Disk column.

[Disk counter parsing](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/SystemProcessSnapshot.cs)

### Rate and presentation

Each process history entry retains the previous cumulative byte count and a
monotonic timestamp. The displayed rate is:

```text
(current disk bytes - previous disk bytes) / elapsed seconds
```

The first sample and any counter reset establish a new baseline and display
zero. A missing extension produces `Unavailable` rather than a measured zero.
Process creation time is part of the row identity, so PID reuse also creates a
new baseline instead of subtracting counters from the previous process.

Disk displays binary megabytes per second under the Windows Task Manager label
`MB/s`:

```text
display value = bytes per second / 1,048,576
```

Positive values below one displayed tenth are clamped to `0.1 MB/s`.

### Cost

Disk does not create another thread, provider, handle, ETW session, or kernel
query. Its incremental collection cost is the extension bounds check, two
64-bit reads, and one addition for each process in the already-returned buffer.
Delta calculation, dynamic storage, search conversion, sorting, and rendering
only occur while Disk is in the active schema.

## Network

### Why adapter counters cannot be reused

The Performance page samples adapter-wide byte counters. Those counters can
calculate total Ethernet or Wi-Fi throughput, but they do not identify which
PID caused the traffic. Dividing or correlating adapter totals with the visible
process list cannot produce correct per-process rates.

The Processes grid instead dynamically loads `srumapi.dll` and registers the
SRUM network provider. This is the same general path used by Windows Task
Manager:

```text
TMTADN
  -> srumapi.dll
  -> COM/RPC
  -> Diagnostic Policy Service
  -> Windows network usage accounting
  -> borrowed SRUM callback records
```

No application-owned kernel ETW session is involved.

[Network collector](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessNetworkUsageSampler.cs)

### Registration and privileges

The collector runs on a dedicated below-normal-priority COM worker. It normally
uses an STA and pumps messages so reverse COM callbacks can be dispatched.

SRUM provider class `0` supplies network records. Registration scope depends on
the process token:

- Scope `1` is available without elevation and returns records filtered to the
  authenticated current user.
- Scope `2` returns all-user records when the app is elevated. If that
  registration fails, TMTADN falls back to scope `1`.

The normal-user path therefore requires neither UAC elevation nor membership
in Performance Log Users. It cannot attribute service, system, or other-user
traffic to their PIDs. SRUM can provide a separate global total, but that total
cannot be redistributed correctly among hidden PIDs.

### Callback records

The callback parser consumes these properties from each current-user record:

| Property ID | Value |
| --- | --- |
| `3` | Sent bytes for the provider interval |
| `4` | Received bytes for the provider interval |
| `6` | PID |

Sent and received bytes are added into a cumulative counter for each PID. The
callback record set is borrowed and is copied synchronously; TMTADN never
retains or frees borrowed callback memory. The initial record set returned by
registration is caller-owned, is not used as a sample, and is freed after
unregistration.

Unregistration is a callback-quiescence barrier. Teardown therefore occurs in
this order:

1. Signal the owning worker.
2. Unregister and wait for outstanding callbacks to drain.
3. Free the registration's initial record set.
4. Release callback state and unload `srumapi.dll`.
5. Uninitialize COM.

### Rate cadence

SRUM callbacks are provider-driven and do not necessarily coincide with the
one-second grid refresh. Every accepted callback receives a generation number
and monotonic timestamp. A process baseline advances only when that generation
changes. Reusing the grid timestamp would otherwise show zero on refreshes
without a callback and a spike when accumulated bytes finally arrive.

The first callback establishes a baseline. A later callback calculates:

```text
(current cumulative network bytes - previous cumulative network bytes)
    / callback elapsed seconds
```

Network displays decimal megabits per second:

```text
display value = bytes per second * 8 / 1,000,000
```

Positive values below one displayed tenth are clamped to `0.1 Mbps`.

### Cost and PID filtering

The Network path is more expensive than Disk because it owns a worker, a COM
registration, reverse RPC callbacks, record parsing, synchronization, and a
PID-to-byte dictionary.

SRUM does not accept a PID whitelist. It produces all records permitted by the
registration scope. Filtering the callback to only the PIDs currently visible
in the viewport would save a small number of local dictionary operations, but
would not reduce kernel accounting, Diagnostic Policy Service work, RPC
traffic, or callback parsing. It would also discard baselines needed when a
process scrolls into view and would prevent correct sorting or searching by
Network across the complete process list.

For those reasons, the callback accumulator tracks every PID supplied by SRUM,
while the grid limits per-row rate calculation and rendering independently.
