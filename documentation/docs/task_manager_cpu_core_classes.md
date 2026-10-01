# Task Manager CPU core classes

The CPU Performance page's Detailed view shows one graph per core class on
hybrid processors, labeled with the core count, for example
`8 Performance Cores`, `8 Efficiency Cores`, and `2 Low Power Efficiency Cores`.
Every input is a record Windows returns from
`GetLogicalProcessorInformationEx`. TMTADN does not read CPUID hybrid leaves,
model-name tables, Thread Director, or a driver for this.

The labels spell the core types out instead of using Intel's P-core, E-core,
and LP E-core abbreviations, because LP already means logical processor
elsewhere in the view.

[Topology reader](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/CPUTopologyReader.cs)
[Core class model](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Models/CPUCoreClassTopology.cs)
[Detailed view](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/UI/Performance/CPUPerformanceDetailedView.cs)

## Windows sources

| Query | Field | Used for |
| --- | --- | --- |
| `RelationProcessorCore` | `PROCESSOR_RELATIONSHIP.EfficiencyClass`, byte 9 of the record | Core class |
| `RelationProcessorCore` | `GroupMask` array | Logical processors of each core |
| `RelationCache` | `Level`, `Type`, and `GroupMasks` of each `CACHE_RELATIONSHIP` | Which cores share an L3 |

Microsoft documents `EfficiencyClass` as: "the intrinsic tradeoff between
performance and power for the applicable core. A core with a higher value for
the efficiency class has intrinsically greater performance and less
efficiency than a core with a lower value for the efficiency class.
EfficiencyClass is only nonzero on systems with a heterogeneous set of
cores." Only the order is meaningful. A performance core is the highest class
present, not class 1; an Alder Lake part with its efficiency cores disabled
reports 0 on every core.

[Processor relationship](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-processor_relationship)
[Cache relationship](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-cache_relationship)

## What Windows reports on Intel hybrid processors

| Processor | Performance cores | Efficiency cores | Low power efficiency cores |
| --- | --- | --- | --- |
| Alder Lake, Raptor Lake | 1 | 0 | none |
| Arrow Lake-S | 1 | 0 | none |
| Meteor Lake | 1 | 0 | 0 |
| Lunar Lake | 1 | none | 0 |
| Panther Lake | 1 | 0 | 0 |

These values come from raw dumps posted by other people, not from hardware
tested here:

- Alder Lake and Raptor Lake: i5-1235U, i7-12700KF, and i5-14600K.
- Arrow Lake-S: Core Ultra 7 265K, whose performance and efficiency cores are
  interleaved in logical-processor numbering.
- Meteor Lake: Core Ultra 7 155H and 155U.
- Lunar Lake and Panther Lake: raw `GetLogicalProcessorInformationEx` buffers
  in Intel's OpenVINO unit tests, identified by their core layout.

[i5-1235U](https://github.com/official-stockfish/fishtest/issues/2450)
[i5-14600K](https://github.com/SimonvBez/CPUSetSetter/issues/3#issuecomment-3474571301)
[i7-12700KF](https://github.com/SimonvBez/CPUSetSetter/issues/3#issuecomment-3492927659)
[Core Ultra 7 265K](https://github.com/SimonvBez/CPUSetSetter/issues/54#issuecomment-3549203517)
[Core Ultra 7 155H](https://community.intel.com/t5/Mobile-and-Desktop-Processors/Detecting-LP-E-Cores-on-Meteor-Lake-in-software/td-p/1577956)
[Core Ultra 7 155U](https://github.com/FlorianZimmer/Retrieve-IntelCPUCoreEfficiencyClass)
[OpenVINO buffers](https://github.com/openvinotoolkit/openvino/blob/master/src/inference/tests/unit/cpu_map_parser/parser_windows.cpp)

`EfficiencyClass` therefore separates performance cores from everything else
but never separates low power efficiency cores from efficiency cores. The
cache records do. Low power efficiency cores sit on a low power island with
their own L2 and no L3, so they fall outside every level-3 mask. Microsoft's
Coreinfo sample on a Core Ultra 7 165U shows the L3 covering logical processors
0 to 11 while 12 and 13 appear only in a 2 MB L2. Intel's own Windows code in
OpenVINO identifies low power efficiency cores the same way.

[Coreinfo](https://learn.microsoft.com/en-us/sysinternals/downloads/coreinfo)
[OpenVINO topology code](https://github.com/openvinotoolkit/openvino/blob/master/src/inference/src/os/win/win_system_conf.cpp)

## What Windows reports on AMD processors

Ryzen hybrid parts pair full cores with compact cores, for example Zen 4 with
Zen 4c in the Ryzen 5 8500G and Zen 5 with Zen 5c in the Ryzen AI 9 HX 370.
Whether Windows gives the compact cores a lower `EfficiencyClass` is unknown.
No raw dump from such a part was checked, and none was available here.

- If Windows reports two classes, the Detailed view shows `Performance Cores`
  and `Efficiency Cores` with no AMD-specific code. Compact cores have their own
  L3, such as the 8 MB on Strix Point's Zen 5c complex, so they are never
  labeled `Low Power Efficiency Cores`.
- If Windows reports one class, those parts show no core-class graphs. The only
  other known source is a core-type field in AMD's extended topology CPUID leaf
  `0x80000026`, which is unverified here. TMTADN does not read it for this,
  because it is not a Windows record.

The X3D labels depend on Windows reporting each CCD's L3 separately. That was
verified only on symmetric hardware: four 32 MB records, one per CCD, on the
Threadripper 9960X. No X3D machine or dump was checked, so the 96 MB and 32 MB
split on a part like the 9950X3D is expected but unverified. Unit tests and the
Ryzen X3D simulation cover the labeling path. See
[Task Manager AMD CCD topology](task_manager_amd_ccd_topology.md).

## Classification

`CPUTopologyReader.ReadCoreClassTopology()` runs once per
`PerformanceSnapshotService`, on every vendor:

1. Normalize the core records exactly as the CCD reader does, so logical
   processor indexes match the utilization array.
2. Mark every logical processor that appears in a level-3 unified cache mask.
3. A core is outside L3 when all of these hold:
   - Windows reported at least one L3.
   - The core is below the highest efficiency class.
   - None of its logical processors is marked.
4. Group cores by efficiency class and L3 placement, highest class first.

The Detailed view adds these graphs after the CCD graphs, only when Windows
reports more than one efficiency class:

| Group | Label |
| --- | --- |
| Highest class | `Performance Core` |
| Lower class, outside L3 | `Low Power Efficiency Core` |
| Any other lower class | `Efficiency Core` |

When more than two distinct classes exist, the lower-class labels append the
Windows class number, for example `8 Efficiency Cores (class 1)`, so no two
graphs share a name. Group utilization is the average of its logical
processors, like a CCD.

The L3 test applies only below the highest class and only when some L3 exists.
A homogeneous processor never splits, and a processor without L3 records never
produces low power efficiency cores.

## What is not used

`SYSTEM_CPU_SET_INFORMATION.SchedulingClass` is not used. It is listed without
a description, first appeared in Windows 10 1803, and behaves as a per-core
performance ranking. On the development Threadripper 9960X, where every core
has efficiency class 0, it takes 16 different values across the 24 cores, so it
cannot name a core type.

Windows 11 Task Manager does not label performance or efficiency cores. Its
per-processor strings are `CPU %d`, `CPU %1!d! (Node %2!d!)`, and
`%s - Parked`.

## Limitations

- Classes are read once per `PerformanceSnapshotService`. Windows has a
  `ProcessorClassUpdate` ETW event, which suggests classes can change at run
  time; TMTADN does not follow such changes.
- Hypervisors can hide or synthesize efficiency classes and caches. The result
  is what Windows reports to the guest.
- Classes are grouped as Windows reports them. A lower-class core that shares
  the L3 is an efficiency core even where the vendor markets it under another
  name.

## Debug simulation

Debug builds add header buttons to the Performance page that swap the Detailed
view's topology:

| Button | Simulated processor | Detailed view graphs |
| --- | --- | --- |
| Live CPU | This machine | As read from Windows |
| Intel hybrid | Core i9-14900K | `8 Performance Cores`, `16 Efficiency Cores` |
| Intel 3-tier hybrid | Core Ultra 7 155H | `6 Performance Cores`, `8 Efficiency Cores`, `2 Low Power Efficiency Cores` |
| Ryzen hybrid | Ryzen AI 9 HX 370 | `4 Performance Cores`, `8 Efficiency Cores` |
| Ryzen X3D | Ryzen 9 9950X3D | `CCD 0 (X3D, 96.0 MB L3)`, `CCD 1 (32.0 MB L3)` |

The Ryzen hybrid row assumes Windows reports two efficiency classes on that
part, which is unknown; it previews the layout only. See
[What Windows reports on AMD processors](#what-windows-reports-on-amd-processors).

Each simulation builds Windows-shaped core, die, and cache records and passes
them through the same `CPUTopologyReader` builders the live path uses.
Clicking a button:

1. Selects the CPU device and switches it to the Detailed view.
2. Replaces the hardware name with `Simulated <processor>`.
3. Outlines the active button.

The simulated graphs plot live utilization. Each simulated logical processor
reads the live processor with the same index, wrapping when the simulated part
has more threads than the machine.

[Simulation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/UI/Performance/CPUArchitectureSimulation.cs)

## Verification

`CPUTopologyReaderTests` covers efficiency-class parsing, class ordering, low
power efficiency core separation, homogeneous systems, missing cache records,
and duplicate processor rejection. `PerformancePageTests` covers every label and each
simulation's graphs. On the Threadripper 9960X, Windows reports one class for
all 24 cores, so the live Detailed view is unchanged.

[Topology tests](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests/CPUTopologyReaderTests.cs)
[Label tests](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests/PerformancePageTests.cs)
