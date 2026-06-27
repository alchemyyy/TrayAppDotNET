# Task Manager performance data

The TMTADN Performance page builds its own snapshots from Windows APIs and
providers. It does not read, scrape, or calibrate against values displayed by
Windows Task Manager. Differences in sample timing, provider behavior, and the
filtering rules below can therefore produce different values. [Snapshot
pipeline](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/PerformanceSnapshotService.cs)

## Common sampling behavior

- An app-owned, below-normal-priority worker starts with TMTADN and continues
  sampling while the window is hidden or another page is selected. The sample
  interval is configurable from 1 to 60,000 milliseconds and defaults to
  1,000 milliseconds.
- CPU, GPU, network, and disk rates use successive counter reads. The first
  sample after application startup establishes a baseline and is not shown as
  a valid rate.
- Every value that can fail has an explicit availability flag. The UI shows
  `Unavailable` or a collecting state instead of treating a missing sample as
  a measured zero.
- Snapshot history is retained even when the Performance page does not exist,
  then replayed when the page opens. History length is configurable from 1 to
  60 minutes and defaults to 1 minute. The maximum retained sample count is
  `history length / sample interval`; time still advances through unavailable
  samples so gaps do not stretch old data across the graph.

[Sampling implementation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/PerformanceSnapshotService.cs)
[Snapshot contract](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Models/PerformanceSnapshot.cs)
[History implementation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Models/PerformanceHistory.cs)

## CPU

Aggregate and per-logical-processor utilization come from successive
`NtQuerySystemInformation(SystemProcessorPerformanceInformation)` reads. TMTADN
computes busy time from the idle, kernel, and user deltas; the highest logical
processor is the maximum valid per-processor result. The first read only primes
the delta baseline. [Windows API](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntquerysysteminformation)
[Implementation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/SystemPerformanceSampler.cs)

Sockets, cores, logical processors, and cache sizes come from
`GetLogicalProcessorInformationEx`. Current and maximum speed are the averages
of the per-processor `ProcessorPowerInformation` values returned by
`CallNtPowerInformation`. Process, thread, handle, commit, cache, and pool
counts come from `K32GetPerformanceInfo`; uptime comes from `GetTickCount64`.
[Topology](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex)
[Power information](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-callntpowerinformation)
[Performance information](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getperformanceinfo)
[Implementation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/SystemPerformanceMetadataReader.cs)

The CPU name is read from
`HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0\ProcessorNameString`.
Virtualization reports the firmware feature flag exposed by
`IsProcessorFeaturePresent`; it is not inferred from utilization data.

On AMD systems, the snapshot also carries exact logical-processor-to-CCD
membership used by the Detailed CPU view's per-CCD graphs. The reader prefers
Windows processor-die records and falls back to affinity-pinned AMD
extended-topology CPUID queries; it does not infer CCD boundaries from shared
L3 masks. [AMD CCD topology](task_manager_amd_ccd_topology.md)

## Memory

Physical total, available, used, and utilization values come from
`GlobalMemoryStatusEx`. Installed RAM comes from
`GetPhysicallyInstalledSystemMemory`. Committed memory, commit limit, cache,
paged pool, and non-paged pool come from the same `K32GetPerformanceInfo`
snapshot used by the CPU view. The Memory row always exists, but unavailable
fields remain marked unavailable. [Physical memory](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex)
[Installed memory](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getphysicallyinstalledsystemmemory)
[Implementation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/SystemPerformanceSampler.cs)

## GPU

Utilization is read from the English PDH wildcard counter
`\GPU Engine(*)\Utilization Percentage`; dedicated and shared usage come from
the corresponding `\GPU Adapter Memory(*)` counters. Process instances for the
same physical engine are summed and capped at 100 percent. Device utilization
is the busiest engine, not the sum of all engines. [PDH wildcard arrays](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw)

DXGI supplies adapter names, capacities, LUIDs, PCI identity fields, and the
software-adapter flag. TMTADN then applies these filters:

1. A PDH adapter tuple must have a matching LUID in the DXGI enumeration.
2. D3DKMT supplies the physical-adapter hardware PNP key for each tuple.
3. Tuples with the same canonical PNP key collapse into one physical GPU row.
4. `ROOT` and `SWD` PNP enumerators are rejected as software-enumerated display
   devices.

[DXGI adapter metadata](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1)
[D3DKMT PNP identity](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_query_physical_adapter_pnp_key)
[Implementation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/GPUPerformanceSampler.cs)

The canonical hardware PNP key is also the preferred persisted ID. A D3DKMT
unique adapter GUID, PCI tuple, and finally boot-local LUID are progressively
weaker fallbacks. If the WDDM PDH counters are unavailable, DXGI metadata may
still produce a GPU row whose live values are unavailable.

## Network

Network rows come directly from `GetIfTable2` and `MIB_IF_ROW2`. Receive and
send rates are deltas of `InOctets` and `OutOctets` divided by monotonic elapsed
time; link speeds, names, descriptions, and interface GUIDs come from the same
row. [Interface table](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/getiftable2)
[Interface fields](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2)

An interface is shown only when all of these are true:

- `HardwareInterface` and `ConnectorPresent` are set.
- Operational status is `Up` and `NotMediaConnected` is clear.
- `FilterInterface` and `EndPointInterface` are clear.
- The interface type is neither software loopback nor tunnel.

This intentionally removes disconnected adapters, loopbacks, tunnels, WAN
miniports, filter bindings, and most virtual or software endpoints. It also
means a real but disconnected NIC is hidden until it connects. The interface
GUID is the preferred persisted ID; the interface LUID is only a fallback.
[Filtering implementation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/NetworkPerformanceSampler.cs)

## Disk

TMTADN calls `QueryDosDeviceW(NULL, ...)` and selects every exposed
`PhysicalDriveN` name. This includes exposed physical disks without a mounted
drive-letter volume and avoids guessing disk numbers with an index-probing
loop. Ready fixed or removable volumes are mapped back to their physical disk
with `IOCTL_STORAGE_GET_DEVICE_NUMBER`; their labels and space totals are then
merged into that disk row. [DOS device enumeration](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-querydosdevicew)
[Volume mapping](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_get_device_number)

Hardware vendor/product text and serial data come from
`IOCTL_STORAGE_QUERY_PROPERTY`. The preferred persisted ID is a deterministic,
device-associated identifier from `StorageDeviceIdProperty` (VPD page 0x83),
followed by the serial number and finally `PhysicalDriveN`. The physical-number
fallback can change after a reboot or hot-plug. [Storage queries](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_query_property)
[Device ID descriptor](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-storage_device_id_descriptor)

Capacity comes from `IOCTL_DISK_GET_DRIVE_GEOMETRY_EX`. Active time, read and
write rates, response time, and queue depth are calculated from successive
`IOCTL_DISK_PERFORMANCE` snapshots. Disk rows are titled `Disk N (C:, D:)` and
show the hardware model as the description; bus labels such as `SSD (NVMe)` are
not part of the presentation. [Disk geometry](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_disk_get_drive_geometry_ex)
[Disk counters](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_disk_performance)
[Implementation](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Services/DiskPerformanceSampler.cs)

## Device order and lifetime

The default category priority is CPU, Memory, GPU, Network, Disk and can be
reordered in settings. A drag stores the visible rows by stable device ID.
Newly discovered or otherwise unconfigured devices are inserted beside their
category according to the configured priority without disturbing explicitly
ordered rows. [Ordering rules](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/Models/PerformanceDeviceOrdering.cs)

When a device disappears, its row and graph history are removed. Its persisted
order ID is retained so it can recover its previous position if the device
returns. Presentation formatting is separate from sampling and does not alter
the measured values. [Page integration](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/UI/PerformancePage.cs)
[Presentation mapping](https://github.com/alchemyyy/TrayAppDotNET/blob/main/TaskManagerTrayAppDotNET/src/UI/PerformanceDevicePresentation.cs)
