# Task Manager CPU frequency

The TMTADN CPU card shows three frequency values. Base speed is read directly
from Windows. Speed and highest recorded speed are measured by Windows from the
processor's hardware cycle counters, which Windows publishes as running totals;
TMTADN divides the change in those totals over a window. Every effective clock
is obtained this way: a processor counts cycles, and a frequency is cycles
divided by time. TMTADN does not model, interpolate, smooth, or clamp the
result.

[Counter reader](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/SystemPerformanceMetadataReader.cs)
[Busy-time accumulation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/ProcessorSpeedEstimator.cs)
[Tests](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests/ProcessorSpeedEstimatorTests.cs)

## Displayed values

| Value | Source |
| --- | --- |
| Base speed | Average of the per-processor `MaxMhz` values from `CallNtPowerInformation` |
| Speed | Highest effective clock among logical processors with enough busy time in the latest sample |
| Highest recorded speed | Highest Speed since TMTADN started |

[Power information](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-callntpowerinformation)
[Processor power structure](https://learn.microsoft.com/en-us/windows/win32/power/processor-power-information-str)

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
| `% Processor Performance` | "The average performance of the processor while it is executing instructions, as a percentage of the nominal performance of the processor. On some processors, Processor Performance may exceed 100%. Some processors are capable of regulating their frequency outside of the control of Windows. Processor Performance will accurately reflect the performance of these processors." |
| `Actual Frequency` | "The current frequency of the processor in megahertz, as measured by the OS. This counter is more accurate than Processor Frequency, which only reflects the frequency the OS requested the processor run at." |
| `Processor Frequency` | "The frequency of the current processor in megahertz. Some processors are capable of regulating their frequency outside of the control of Windows. Processor Frequency will not accurately reflect actual processor frequency on these systems. Use % Processor Performance or Actual Frequency instead." |

`% Processor Performance` and `Actual Frequency` both have counter type
`0x40020500` (`PERF_AVERAGE_BULK`, reported by `Get-Counter` as
`AverageCount64`). Each logical-processor instance is a pair of running totals:

- A 64-bit numerator: busy time weighted by the measured performance, as a
  percentage for `% Processor Performance` and in megahertz for
  `Actual Frequency`.
- A 32-bit base: busy time, in the counter's own units.

The displayed value of such a counter is always a ratio of changes:

```text
value = (numerator now - numerator before) / (base now - base before)
```

TMTADN reads the raw `% Processor Performance` pairs for every
`group,number` instance with `PdhGetRawCounterArrayW` and skips the `_Total`
and per-group total instances. It also reads the base of
`\Processor Information(_Total)\% Processor Utility`, which counts elapsed time
in the same units, to convert busy-time units to seconds.

[Raw counter arrays](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetrawcounterarrayw)
[Raw counter values](https://learn.microsoft.com/en-us/windows/win32/api/pdh/ns-pdh-pdh_raw_counter)

## Calculation

```text
For each logical processor, between two collections:
    weighted delta = numerator now - numerator before
    busy delta     = (base now - base before) mod 2^32
    pending weighted += weighted delta
    pending busy     += busy delta

For each collection:
    seconds per unit = Stopwatch elapsed seconds
                       / ((elapsed base now - elapsed base before) mod 2^32)

When pending busy * seconds per unit >= 0.020:
    performance percent = pending weighted / pending busy
    core speed          = base speed * performance percent / 100
    pending weighted = 0
    pending busy     = 0

Speed = highest core speed completed in this collection,
        otherwise the previous Speed
Highest recorded speed = highest Speed since TMTADN started
```

The counter units are not assumed. They ran at 625,000 per second on the test
system, but TMTADN recalibrates seconds per unit on every collection from the
elapsed-time base and the monotonic Stopwatch.

## Why a busy-time threshold exists

When a logical processor ran for only microseconds during a window, both deltas
are tiny and their ratio stops meaning anything. The numerator and base are not
advanced in lockstep, and a timing difference that is negligible over 20 ms of
busy time dominates over a few microseconds. The test system logged these
per-core maxima over five-minute runs:

| Sampling interval | No threshold | 5 ms busy time | 20 ms busy time |
| --- | ---: | ---: | ---: |
| 100 ms | 816.8% | 135.6% | 134.2% |
| 250 ms | 296.2% | 134.2% | 134.2% |
| 750 ms | 132.8% | 132.8% | 132.8% |
| 1,000 ms | 132.4% | 132.4% | 132.4% |

The 816.8% reading came from a logical processor with 10 busy-time units, about
16 microseconds. A ten-minute run at 100 ms without a threshold reached
1,190.1%, a 50.0 GHz reading. Longer intervals make these windows rarer, not
impossible: before the threshold existed, a session sampling every 750 ms for
about 16 hours recorded a highest speed of 19.05 GHz, a single 453.6% reading
that highest recorded speed then kept.

With the 20 ms threshold, no interval exceeded 134.2%. Running the full reader
afterward for three minutes produced at most 5.63 GHz at 100 ms across 1,650
samples and 5.57 GHz at 750 ms across 237 samples.

## Behavior without a fresh value

- A lightly loaded core accumulates busy time across collections until it
  reaches 20 ms, so its reading is its average over that longer window.
- When no logical processor completes a reading in a collection, Speed keeps
  the previous measured value. It can be stale, but it is never invented.
- Until the first reading completes, after startup or a sampling reset, Speed
  falls back to the highest `CurrentMhz` from `CallNtPowerInformation`. Windows
  defines that field as "the maximum specified processor clock frequency
  multiplied by the current processor throttle", so it does not follow boost.
- The bases are 32-bit and wrap. At 625,000 units per second, the elapsed-time
  base wraps about every 1.9 hours and each busy-time base after 1.9 hours of
  busy time. Deltas are taken modulo 2^32.
- A numerator that moves backwards restarts that processor's accumulation. A
  sampling reset clears every processor's accumulation.

## Values that look direct but are not used

On the test system, each of the first three sources read 4,200 MHz while the
same cores measured about 5.5 GHz.

| Source | Why it is not used |
| --- | --- |
| `CurrentMhz` from `CallNtPowerInformation` | Nominal frequency multiplied by the current throttle, so it never shows boost |
| `Win32_Processor.CurrentClockSpeed` | Reports the same nominal value |
| `\Processor Information(*)\Processor Frequency` | Windows states it does not reflect actual frequency on processors that regulate their own clocks |
| Model-specific registers | Require a kernel-mode driver. `APERF` and `MPERF` still need the same delta division, and AMD's hardware P-state status register decodes to the frequency setting at the moment it is read, not the clock delivered over a window |

## Actual Frequency

`Actual Frequency` publishes the same measurement in megahertz. On the test
system it agreed with `base speed * % Processor Performance / 100` within 66 MHz
across 480 one-second per-core comparisons, and in 397 of them both counters
reported an identical busy-time delta. Because it is the same counter type, it
has the same short-window behavior: at a 100 ms interval it reported 17,368 MHz
for a logical processor with 12 busy-time units, about 19 microseconds.

TMTADN currently reads `% Processor Performance` and scales it by the base
speed. Reading `Actual Frequency` instead would still need the busy-time
threshold, but it would remove the dependence on the averaged base speed
described below.

## Interpretation and limitations

- Speed is the effective clock while executing, averaged over busy time. Idle
  time does not lower it, and it is not an instantaneous clock reading.
- Windows Task Manager shows one Speed for the whole processor. TMTADN reports
  the busiest qualifying logical processor, so its Speed is normally higher.
- Base speed is the average `MaxMhz` across logical processors. On hybrid
  processors whose core types have different nominal frequencies, per-core
  speeds are therefore approximate.
- Per-core readings reached 134.2% of base on the test system. TMTADN reports
  them as measured and does not clamp to a rated maximum.

The test system was an AMD Ryzen Threadripper 9960X (24 cores, 48 logical
processors, 4.2 GHz base) on Windows 11 build 26200.
