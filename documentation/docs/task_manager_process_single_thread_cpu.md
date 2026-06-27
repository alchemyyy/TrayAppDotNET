# Task Manager process single-thread CPU data

The TMTADN Processes grid provides two CPU percentages with different
denominators:

- **CPU** is the process's aggregate scheduled CPU time divided by the total
  capacity of all logical processors.
- **CPU (single)** is the highest scheduled CPU usage of any one thread in the
  process.

`CPU (single)` is intended to expose serialized CPU bottlenecks. A process can
show a low total-machine CPU percentage while one thread is continuously using
all of the scheduling time available to a single logical processor.

[Process snapshot service](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessSnapshotService.cs)
[Thread CPU tracker](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessThreadCPUTracker.cs)

## Data source

The process sampler already calls
`NtQuerySystemInformation(SystemProcessInformation)` once per refresh. Each
returned process entry contains an array of `SYSTEM_THREAD_INFORMATION`
records. The full-process variant uses
`SYSTEM_EXTENDED_THREAD_INFORMATION`, whose first member is the same base
thread record.

The single-thread calculation reads these cumulative fields from each thread:

| Field | Purpose |
| --- | --- |
| `ClientId.UniqueThread` | Thread ID |
| `CreateTime` | Distinguishes a new thread that reused an old thread ID |
| `KernelTime` | Cumulative scheduled kernel-mode CPU time |
| `UserTime` | Cumulative scheduled user-mode CPU time |

The per-thread cumulative counter is:

```text
total thread CPU ticks = KernelTime + UserTime
```

Both times use 100-nanosecond ticks. Addition saturates at the maximum signed
64-bit value instead of overflowing.

[System process snapshot](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/SystemProcessSnapshot.cs)

## Calculation

Each process owns a thread CPU tracker. For every current thread, the tracker
looks up the preceding sample by the pair `(thread ID, thread creation time)`.
When a preceding sample exists, it calculates:

```text
processor seconds =
    (current thread CPU ticks - previous thread CPU ticks)
    / TimeSpan.TicksPerSecond

elapsed seconds =
    (current Stopwatch timestamp - previous Stopwatch timestamp)
    / Stopwatch.Frequency

thread CPU percent =
    clamp(processor seconds / elapsed seconds * 100, 0, 100)

CPU (single) = maximum thread CPU percent in the process
```

A thread cannot execute simultaneously on multiple logical processors, so its
scheduled-time utilization is capped at 100%. The process value is the maximum
rather than the sum. Multiple busy threads therefore increase the ordinary
`CPU` value but do not make `CPU (single)` exceed 100%.

### Example on 24 logical processors

| Process activity during the interval | CPU | CPU (single) |
| --- | ---: | ---: |
| One thread at 100% | About 4.2% | 100% |
| One thread at 50% | About 2.1% | 50% |
| Four threads at 50% each | About 8.3% | 50% |
| Two threads at 100% each | About 8.3% | 100% |

The ordinary `CPU` calculation divides the process's aggregate processor-time
delta by `Environment.ProcessorCount`. `CPU (single)` does not use the
aggregate process counter; it independently compares each thread's counter.

## Baselines and thread lifecycle

The calculation requires two samples. The following cases establish a new
baseline and contribute zero for that interval:

- The column has just been activated.
- A process or thread has just appeared.
- A thread ID has been reused with a different creation time.
- A cumulative counter moved backwards.
- The monotonic timestamp did not advance.

Threads absent from the current process snapshot are removed from that
process's tracker. If their IDs are reused later, the new threads cannot inherit
the removed counters. Process identity separately includes PID and process
creation time, so PID reuse also starts with an empty thread tracker.

If no thread samples can be read, the tracker returns zero and removes its old
thread baselines. The next successful sample then establishes fresh baselines
instead of calculating a spike across a collection gap.

## Activation and sampling scope

`CPU (single)` is optional and hidden by default. Its data becomes active when
either of these is true:

- The user enables the column.
- A process-search expression references the column, such as
  `{CPU (single)}>=90%`.

The calculation follows the existing warm-process policy. New process rows are
sampled to establish baselines. Subsequent refreshes normally update warm rows,
while operations that require complete process ordering or filtering can
request samples for every process.

Disabling the column removes it from the active schema. A schema change clears
process histories, including all per-thread baselines.

## Cost

The normal path does not start ETW, create another worker, issue another system
process query, or open every thread. It walks the thread structures already
present in the process snapshot only while `CPU (single)` is active.

Runtime state consists of:

- One reusable thread-sample array sized for the largest sampled process.
- One lazily created thread-history dictionary per sampled process.
- One stale-key list per tracker for removing exited threads without allocating
  on every refresh.

If the system snapshot query fails and the service falls back to
`System.Diagnostics.Process`, it reads `ProcessThread.Id`,
`ProcessThread.StartTime`, and `ProcessThread.TotalProcessorTime`. This fallback
may open thread handles, but it is not used during the normal snapshot path.

## Interpretation and limitations

`CPU (single)` answers: **How busy was this process's busiest individual thread
during the sampling interval?**

It does not identify a processor number and does not prove that one specific
logical processor stayed busy. Windows can migrate a thread between logical
processors during the interval. That migration does not reduce the value's
usefulness for detecting a single-thread bottleneck because the same thread
still cannot execute concurrently on more than one processor.

The value is not an exact per-process contribution to the busiest logical
processor. Multiple threads from the same process can time-share one logical
processor, and periodic thread-time snapshots do not retain that scheduling
placement. Exact processor attribution would require scheduler context-switch
tracing.

The value is also scheduled time rather than frequency-adjusted work. CPU
frequency changes and differences between hybrid cores do not alter this
percentage. Short spikes are averaged across the sampling interval.
