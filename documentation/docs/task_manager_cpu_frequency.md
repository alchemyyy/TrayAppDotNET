# Task Manager CPU frequency

The TMTADN CPU card shows three frequency values. Base speed is read directly
from Windows. Speed is the value Windows Task Manager shows: Windows' own
measurement of the clock the logical processors ran at while executing,
averaged over all of them. Highest recorded speed is the highest Speed since
TMTADN started. Windows measures the clock from the processor's hardware cycle
counters and publishes it as running totals; TMTADN divides the change in those
totals over a window. Every effective clock is obtained this way: a processor
counts cycles, and a frequency is cycles divided by time. TMTADN does not
model, interpolate, smooth, or clamp the result.

[Counter reader](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/SystemPerformanceMetadataReader.cs)
[Busy-time accumulation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessorFrequencyAccumulator.cs)
[Tests](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests/ProcessorFrequencyAccumulatorTests.cs)

## Displayed values

| Value | Source |
| --- | --- |
| Base speed | Average of the per-processor `MaxMhz` values from `CallNtPowerInformation` |
| Speed | `\Processor Information(_Total)\Actual Frequency` over the latest window with enough busy time |
| Highest recorded speed | Highest Speed since TMTADN started |

[Power information](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-callntpowerinformation)
[Processor power structure](https://learn.microsoft.com/en-us/windows/win32/power/processor-power-information-str)

## What Windows Task Manager shows

The rules below were read from `Taskmgr.exe` 10.0.26100.9278 with its public
symbols, on Windows 11 build 26200.9457.

`AtmCpuView::UpdateCpuSpeed` formats a value, in gigahertz with two decimals,
that `WdcCpuMonitor` fills from one of two code paths. Task Manager feature
59416109 selects the path:

- Feature off: `WdcCpuMonitor::UpdateQuery_BeforeCpuUtilization` reads the
  `Processor Information` counter set through the kernel counter interface
  (`PcwCreateQuery`, then `PcwAddQueryItem` for instance `*`, then
  `PcwCollectData`). For the `_Total` instance it divides the change in counter
  33 by the change in counter 34, then divides by 1,000. When counter 34 did
  not change it shows 0.10 GHz instead.
- Feature on: `WdcCpuMonitor::UpdateProcessorFrequency` shows the mean
  `CurrentMhz` from `CallNtPowerInformation`, divided by 1,000.

`PerfQueryCounterSetRegistrationInfo` names counter 33 `Actual Frequency`, type
`0x40020500` with base counter 34. On the test system every `CurrentMhz` read
4,200 while Task Manager showed 4.94 GHz, so the counter path is the one in
use there.

TMTADN reads the same `_Total` counter pair through PDH with
`PdhGetRawCounterValue`, which returns the same 64-bit numerator and 32-bit
base.

## Where the measurement comes from

### Processor cycle counters

Each x86 core has two free-running counters that advance only while the core
executes instructions:

- `MPERF` advances at a fixed reference rate, the nominal frequency.
- `APERF` advances at the core's actual clock.

Over any window, the core's effective clock while executing is:

```text
effective clock = nominal frequency * (APERF delta / MPERF delta)
```

ACPI CPPC exposes the same pair as the delivered and reference performance
counters. Reading these registers directly requires a kernel-mode driver, so
TMTADN reads the measurement Windows publishes instead.

### Windows performance counters

Windows publishes the measurement through the `Processor Information` counter
set. The descriptions below are Windows' own help text, read through
`PdhGetCounterInfoW`:

| Counter | Windows description |
| --- | --- |
| `Actual Frequency` | "The current frequency of the processor in megahertz, as measured by the OS. This counter is more accurate than Processor Frequency, which only reflects the frequency the OS requested the processor run at." |
| `% Processor Performance` | "The average performance of the processor while it is executing instructions, as a percentage of the nominal performance of the processor. On some processors, Processor Performance may exceed 100%. Some processors are capable of regulating their frequency outside of the control of Windows. Processor Performance will accurately reflect the performance of these processors." |
| `Processor Frequency` | "The frequency of the current processor in megahertz. Some processors are capable of regulating their frequency outside of the control of Windows. Processor Frequency will not accurately reflect actual processor frequency on these systems. Use % Processor Performance or Actual Frequency instead." |

`Actual Frequency` and `% Processor Performance` both have counter type
`0x40020500` (`PERF_AVERAGE_BULK`, reported by `Get-Counter` as
`AverageCount64`). Each instance is a pair of running totals:

- A 64-bit numerator: busy time weighted by the measured clock, in megahertz
  for `Actual Frequency` and as a percentage for `% Processor Performance`.
- A 32-bit base: busy time, in the counter's own units.

The displayed value of such a counter is always a ratio of changes:

```text
value = (numerator now - numerator before) / (base now - base before)
```

TMTADN also reads the base of `\Processor Information(_Total)\% Processor
Utility`, which counts elapsed time in the same units, to convert busy-time
units to seconds.

[Raw counter values](https://learn.microsoft.com/en-us/windows/win32/api/pdh/ns-pdh-pdh_raw_counter)

### The _Total instance

The `_Total` base is the mean of the logical processor bases, and its ratio is
the ratio of the per-processor sums. Over four one-second windows on the test
system, the per-processor bases summed to 47.99 to 48.02 times the `_Total`
base change across 48 logical processors, and the two ratios agreed within
1.4 MHz. Speed is therefore the busy-time-weighted mean clock of all logical
processors: a processor that was busy longer counts for more, and idle time
does not lower it.

## Calculation

```text
Between two collections:
    weighted delta = numerator now - numerator before
    busy delta     = (base now - base before) mod 2^32
    pending weighted += weighted delta
    pending busy     += busy delta

For each collection:
    seconds per unit = Stopwatch elapsed seconds
                       / ((elapsed base now - elapsed base before) mod 2^32)

When pending busy * seconds per unit >= 0.020:
    Speed in MHz     = pending weighted / pending busy
    pending weighted = 0
    pending busy     = 0

Highest recorded speed = highest Speed since TMTADN started
```

The counter units are not assumed. They ran at 625,000 per second on the test
system, but TMTADN recalibrates seconds per unit on every collection from the
elapsed-time base and the monotonic Stopwatch.

## Why a busy-time threshold exists

When a window holds only microseconds of busy time, both deltas are tiny and
their ratio stops meaning anything. The numerator and base are not advanced in
lockstep, and a timing difference that is negligible over 20 ms of busy time
dominates over a few microseconds. Single logical processors on the test system
logged these maxima of `% Processor Performance` over five-minute runs:

| Sampling interval | No threshold | 5 ms busy time | 20 ms busy time |
| --- | ---: | ---: | ---: |
| 100 ms | 816.8% | 135.6% | 134.2% |
| 250 ms | 296.2% | 134.2% | 134.2% |
| 750 ms | 132.8% | 132.8% | 132.8% |
| 1,000 ms | 132.4% | 132.4% | 132.4% |

The 816.8% reading came from a logical processor with 10 busy-time units, about
16 microseconds. A ten-minute run at 100 ms without a threshold reached
1,190.1%, a 50.0 GHz reading, and `Actual Frequency` behaved the same way: at
100 ms it reported 17,368 MHz for a logical processor with 12 busy-time units.
Longer intervals make these windows rarer, not impossible: a session sampling
every 750 ms for about 16 hours without a threshold recorded a single 453.6%
reading, a 19.05 GHz highest speed.

The `_Total` base is a per-processor mean, so the 20 ms threshold requires the
average logical processor to have been busy for 20 ms in the window. At the
default one-second interval that is 2% average busy time; on the test system
the `_Total` base advanced by 47 to 116 ms per second. Task Manager applies no
threshold, but TMTADN's sampling interval goes down to 1 ms, where a `_Total`
window holds only microseconds of busy time.

## Behavior without a fresh value

- A lightly loaded processor accumulates busy time across collections until
  the window reaches 20 ms, so its reading is the average over that longer
  window.
- When no window qualifies in a collection, Speed keeps the previous measured
  value. It can be stale, but it is never invented.
- Until the first window qualifies, after startup or a sampling reset, Speed
  falls back to the mean `CurrentMhz` from `CallNtPowerInformation`, the value
  Task Manager's other code path shows. Windows defines that field as "the
  maximum specified processor clock frequency multiplied by the current
  processor throttle", so it does not follow boost.
- The bases are 32-bit and wrap. At 625,000 units per second, the elapsed-time
  base wraps about every 1.9 hours and the busy-time base after 1.9 hours of
  mean busy time. Deltas are taken modulo 2^32.
- A numerator that moves backwards restarts the window. A sampling reset
  clears it.

## Values that look direct but are not used

On the test system, each of the first three sources read 4,200 MHz while the
same cores measured about 5.5 GHz.

| Source | Why it is not used |
| --- | --- |
| `CurrentMhz` from `CallNtPowerInformation` | Nominal frequency multiplied by the current throttle, so it never shows boost. Used only until the first measured window |
| `Win32_Processor.CurrentClockSpeed` | Reports the same nominal value |
| `\Processor Information(*)\Processor Frequency` | Windows states it does not reflect actual frequency on processors that regulate their own clocks |
| Base speed times `% Processor Performance` | The same measurement as a percentage of nominal; it agreed with `Actual Frequency` within 66 MHz across 480 one-second per-core comparisons, but scaling it by the averaged `MaxMhz` adds a dependency that `Actual Frequency` does not have |
| Model-specific registers | Require a kernel-mode driver. `APERF` and `MPERF` still need the same delta division, and AMD's hardware P-state status register decodes to the frequency setting at the moment it is read, not the clock delivered over a window |

## Interpretation and limitations

- Speed is the effective clock while executing, averaged over busy time and
  over all logical processors. Idle time does not lower it, and it is not an
  instantaneous clock reading.
- One fast core is diluted by the others, so individual cores can run well
  above Speed.
- Readings above base speed are reported as measured and are not clamped to a
  rated maximum.

The test system was an AMD Ryzen Threadripper 9960X (24 cores, 48 logical
processors, 4.2 GHz base) on Windows 11 build 26200.
