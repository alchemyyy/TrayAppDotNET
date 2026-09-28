# Task Manager AMD CCD topology

TMTADN has a topology reader that maps every active Windows
logical processor to an exact AMD Core Complex Die (CCD). The mapping is
used for per-CCD metric aggregation in the CPU Performance page's Detailed
view. Each CCD also carries the level-3 cache size Windows reports inside it,
which is how the Detailed view labels the stacked-cache CCD of an X3D part.

The implementation does not query an AMD chipset driver, Ryzen Master, the
SMU, or a privileged device. It uses Windows processor-topology records first
and an affinity-pinned AMD CPUID query as a fallback. Both paths work from a
normal non-elevated process and in Debug and Release builds.

The same reader also groups cores by the efficiency class Windows reports on
every vendor; see [Task Manager CPU core classes](task_manager_cpu_core_classes.md).

[Topology reader](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/CPUTopologyReader.cs)
[Topology model](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Models/CPUCCDTopology.cs)

## Scope and exactness policy

The reader reports a topology only when it can establish complete, exact
membership from one of these sources:

1. Windows `RelationProcessorDie` records.
2. AMD extended CPU topology leaf `CPUID 0x80000026`, die level type 3.

It deliberately does not infer CCDs from:

- shared L3 cache masks;
- NUMA nodes;
- processor model-name tables;
- assumed core counts or contiguous processor-number ranges.

L3 sharing is useful on some processors, but it is not an architectural CCD
identifier. A CCD can expose more than one L3 domain, and virtualized or future
topologies can present cache boundaries that do not match physical dies. If
neither exact source is available, the reader returns `CPUCCDTopology.Empty`.
Consumers must omit the per-CCD view instead of fabricating a partition.

The system is AMD-only. `CPUTopologyReader.ReadCCDTopology()` first verifies
the `AuthenticAMD` CPUID vendor string and returns an unavailable topology for
other vendors.

## Runtime data flow

```text
PerformanceSnapshotService construction
    |
    +-- CPUTopologyReader.ReadCCDTopology()
    |       |
    |       +-- Verify AuthenticAMD
    |       +-- Read RelationProcessorCore
    |       +-- Prefer RelationProcessorDie
    |       +-- Otherwise probe CPUID 0x80000026 on every logical processor
    |       +-- Normalize and validate the complete topology
    |       +-- Attribute RelationCache level-3 sizes to the finished CCDs
    |
    +-- Store one immutable CPUCCDTopology instance
            |
            +-- Attach it to every CPUPerformanceSnapshot
```

Topology is read once per `PerformanceSnapshotService` lifetime. Processor-to-
CCD membership is boot-stable, so it is not recomputed during each performance
sample. Each `CPUPerformanceSnapshot.CCDTopology` references the stored
topology.

[Snapshot integration](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/PerformanceSnapshotService.cs)
[Snapshot contract](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Models/PerformanceSnapshot.cs)

## Preferred Windows path

The preferred path calls
[`GetLogicalProcessorInformationEx`](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex)
twice with explicit relationship types:

- `RelationProcessorCore` supplies the logical processors belonging to every
  physical core.
- `RelationProcessorDie` supplies the logical processors belonging to every
  processor die.

The die relationship is queried directly rather than extracted from a
`RelationAll` result. During implementation validation, a Windows 11 system
returned four correct die records for a direct `RelationProcessorDie` query
while omitting those records from `RelationAll`.

Each variable-sized `SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX` record contains
one or more `GROUP_AFFINITY` masks. The parser expands those masks into Windows
`(Group, Number)` processor identities. It supports relationships spanning
multiple processor groups and does not assume that the machine fits in one
64-bit affinity mask.

Windows provides exact membership but does not provide the AMD hardware
topology number used by the CPUID fallback. Therefore entries obtained from
this path have `HardwareTopologyID = null`.

## AMD CPUID fallback

The fallback runs only when the Windows core relationships are valid but a
complete Windows die topology is unavailable. It also requires the processor
to advertise extended CPUID function `0x80000026`.

CPUID reports the topology of the logical processor executing the instruction,
so one unpinned query is insufficient. The reader creates a short-lived
background probe thread and, for every active logical processor:

1. Calls `SetThreadGroupAffinity` with that processor's group and one-bit mask.
2. Calls `GetCurrentProcessorNumberEx` to verify that the thread is running on
   the requested logical processor.
3. Enumerates `CPUID 0x80000026` subleaves until the terminating level.
4. Selects topology level type 3, the die or CCD level.
5. Derives the hardware domain ID as `EDX >> (EAX & 0x1f)`.

Logical processors with the same derived domain ID are members of the same
CCD. These generated die relationships then pass through the same
normalization and validation code as the Windows path.

The affinity change applies only to the probe thread. No process-wide affinity
is changed. `SetThreadGroupAffinity`, `GetCurrentProcessorNumberEx`, and user-
mode CPUID execution do not require administrator rights.

[Thread group affinity](https://learn.microsoft.com/en-us/windows/win32/api/processtopologyapi/nf-processtopologyapi-setthreadgroupaffinity)
[Current processor query](https://learn.microsoft.com/en-us/windows/win32/api/processtopologyapi/nf-processtopologyapi-getcurrentprocessornumberex)
[.NET CPUID intrinsic](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.x86base.cpuid)

## Normalized data model

`CPUCCDTopology` contains four relevant values:

| Member | Meaning |
| --- | --- |
| `Source` | `WindowsProcessorDie`, `AMDExtendedCPUTopology`, or `None` |
| `LogicalProcessors` | Every active logical processor in system-index order |
| `Cores` | Every physical core and its logical-processor membership |
| `CCDs` | Every CCD and its core and logical-processor membership |

The entry types expose these fields:

| Type | Fields |
| --- | --- |
| `CPULogicalProcessor` | `SystemIndex`, Windows `Group`, Windows `Number` |
| `CPUCoreTopologyEntry` | `CoreIndex`, `CCDIndex`, `LogicalProcessorIndexes` |
| `CPUCCDTopologyEntry` | `CCDIndex`, optional `HardwareTopologyID`, `CoreIndexes`, `LogicalProcessorIndexes`, `L3CacheBytes` |

Logical processors are sorted first by processor group and then by processor
number. `SystemIndex` is their zero-based position in that order. Cores and
CCDs are sorted by their lowest logical-processor index, which produces
deterministic `CoreIndex` and `CCDIndex` values independent of native record
order.

`CCDIndex` is the application-facing display and array index. It is not a
firmware CCD label. `HardwareTopologyID` is populated only by the CPUID path
and should be treated as diagnostic topology data, not as a stable persisted
identifier.

## Validation invariants

The topology builder rejects the entire result unless all of these conditions
hold:

- At least one core and one CCD relationship exist.
- Every relationship contains at least one nonzero group mask.
- A relationship cannot repeat a processor group.
- Every active logical processor belongs to exactly one physical core.
- Every active logical processor belongs to exactly one CCD.
- Core and CCD coverage contain the same complete logical-processor set.
- No physical core is split across CCDs.
- No logical processor appears twice in either partition.

This all-or-nothing validation prevents a partial mapping from silently
misattributing sampled metrics. Native parsing errors, unsupported hardware,
affinity failures, CPUID inconsistencies, and unexpected exceptions all
produce `CPUCCDTopology.Empty`. Exceptions are also written to the TADN log.

## Per-CCD graph aggregation

CPU snapshots contain per-logical-processor utilization in
`LogicalProcessorUtilizationPercents`. The Detailed view:

1. Builds one graph group per CCD, in `CCDIndex` order, when the topology is
   available and has more than one CCD. Core-class groups follow them.
2. Requires `CPUPerformanceSnapshot.HasUtilizationSample` for every sample.
3. Uses each group's `LogicalProcessorIndexes` as indexes into
   `LogicalProcessorUtilizationPercents`.
4. Averages those logical-processor percentages for the group's utilization.
5. Preserves the existing unavailable-sample behavior in graph history.

Do not use `CoreIndexes` to index the utilization array. They describe
physical-core membership, while the sampled utilization array contains one
value per logical processor. For future per-logical-processor metrics, the
same membership indexes can be reused. Whether values should be averaged,
summed, or reduced by another operation depends on the metric's semantics.

Before indexing, a consumer should still verify that the sample array covers
all topology indexes. A mismatch means the sample and topology refer to
different active processor sets and that sample should be treated as
unavailable for per-CCD presentation.

## Level-3 cache per CCD and X3D labels

An X3D processor stacks extra L3 on one CCD. Windows reports that directly:
`GetLogicalProcessorInformationEx(RelationCache)` returns one
`CACHE_RELATIONSHIP` per cache instance with its level, type, size in bytes,
and the logical processors that share it. After the CCD partition is final,
from either source above, the reader queries `RelationCache` directly and:

1. Keeps level-3 unified caches.
2. Maps every processor in each cache mask to its CCD.
3. Adds the cache size to that CCD's `L3CacheBytes`. Sizes are summed because
   one CCD can hold two L3 domains, as on Zen 2.
4. Leaves every CCD at zero if any L3 spans two CCDs or names a processor
   outside the topology.

The L3 masks never decide CCD membership, so the exactness policy above still
holds. They only attach a Windows-reported size to CCDs that were already
established exactly.

| Record field | Byte offset in the record |
| --- | --- |
| `Level` | 8 |
| `CacheSize` | 12 |
| `Type` (`CacheUnified` is 0) | 16 |
| `GroupCount` | 38 |
| `GroupMasks` | 40 |

`GroupCount` replaced reserved bytes when caches began spanning processor
groups, so older Windows builds leave it zero with a single mask; the parser
reads one mask in that case.

The Detailed view compares the CCD sizes:

- If every CCD has a size and the sizes are not all equal, each CCD label shows
  its L3 size, and every CCD larger than the smallest is labeled X3D, for
  example `CCD 0 (X3D, 96.0 MB L3)` and `CCD 1 (32.0 MB L3)`.
- Otherwise the labels stay `CCD 0`, `CCD 1`, and so on.

Windows exposes only the size, not whether it came from stacked die, so the
label is relative:

- A part whose CCDs all carry stacked cache reports equal sizes and gets no
  X3D label.
- A single-CCD X3D part shows no CCD graphs at all, as before.

NOTE: this was verified only on symmetric hardware. No X3D machine or dump was
checked, so Windows reporting 96 MB and 32 MB for the two CCDs of a part like
the 9950X3D is expected but unverified.

## Platform limitations

- Only active, scheduler-visible logical processors are represented. Cores
  disabled by firmware are absent.
- A CPU I/O die has no schedulable logical processors and is not represented as
  a CCD entry.
- Hypervisors can hide or synthesize topology. The result is the exact topology
  exposed to the Windows guest, not necessarily the host's physical layout.
- A restrictive job or process environment can prevent the fallback probe
  thread from reaching every logical processor. The reader then returns an
  unavailable topology rather than a partial result.
- Older AMD processors that do not expose Windows die relationships or CPUID
  leaf `0x80000026` remain unsupported by design because the excluded fallback
  techniques would be heuristic.

## Verification

`CPUTopologyReaderTests` covers native relationship parsing, deterministic
ordering, processor groups, duplicate and incomplete membership rejection,
core containment, CPUID die-level decoding, cache record parsing, L3
attribution, live discovery, the forced CPUID fallback, and snapshot
integration. `PerformancePageTests` covers the CCD and X3D labels.

[Topology tests](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests/CPUTopologyReaderTests.cs)
[Label tests](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/tests/TaskManagerTrayAppDotNET.Tests/PerformancePageTests.cs)

The implementation was validated in a non-elevated Debug test run and a Native
AOT publish. On the development AMD Ryzen Threadripper 9960X system, the
preferred Windows path reported 48 logical processors, 24 physical cores, and
4 CCDs. The forced CPUID fallback independently produced a complete topology
on the same system. Windows reported one 32 MB L3 per CCD there, so the labels
stay plain; Debug builds can preview the X3D labels as described in
[Task Manager CPU core classes](task_manager_cpu_core_classes.md).
