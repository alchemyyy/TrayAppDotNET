using TrayAppDotNETCommon.Interop.Wmi;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads physical DIMM metadata through the Common WMI client.</summary>
/// <remarks>
/// The read runs as a separate task so the performance sampler thread never waits on WMI, and every WMI wait is
/// bounded. A failed read is retried with exponential backoff instead of being remembered for the session.
/// <see cref="Get"/> is called from one thread (the sampler); it only starts reads and picks up finished ones.
/// </remarks>
internal sealed class PhysicalMemoryMetadataReader
{
    private const string WmiNamespace = @"ROOT\CIMV2";

    private const string PublicModuleQuery =
        "SELECT BankLabel, Capacity, ConfiguredClockSpeed, Speed, FormFactor, PartNumber "
        + "FROM Win32_PhysicalMemory";

    private const string PrivateModuleQuery =
        "SELECT BankLabel, Capacity, ConfiguredClockSpeed, Speed, FormFactor, PartNumber, SerialNumber "
        + "FROM Win32_PhysicalMemory";

    private const string SlotQuery =
        "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray WHERE Use = 3";

    private const long InitialRetryDelayMilliseconds = 5_000;
    private const long MaximumRetryDelayMilliseconds = 10 * 60 * 1_000;

    private readonly MetadataSlot _public = new();
    private readonly MetadataSlot _private = new();

    /// <summary>
    /// Returns the cached module metadata for the privacy mode, starting or retrying a background read when needed.
    /// Until the first read finishes the metadata is empty and the error is null.
    /// </summary>
    public PhysicalMemoryHardwareMetadata Get(bool includeSerialNumbers, out string? error)
    {
        if (!includeSerialNumbers)
        {
            Update(_public, includeSerialNumbers: false);
            error = _public.Error;
            return _public.Metadata;
        }

        Update(_private, includeSerialNumbers: true);
        if (_private.Succeeded)
        {
            error = null;
            return _private.Metadata;
        }

        Update(_public, includeSerialNumbers: false);
        error = _private.Error ?? _public.Error;
        return _public.Metadata;
    }

    /// <summary>Waits for any read in flight; lets tests observe a completed read deterministically.</summary>
    internal bool WaitForPendingReads(TimeSpan timeout)
    {
        Task?[] pending = [_public.Pending, _private.Pending];
        foreach (Task? task in pending)
        {
            if (task != null && !task.Wait(timeout)) return false;
        }

        return true;
    }

    private void Update(MetadataSlot slot, bool includeSerialNumbers)
    {
        if (slot.Succeeded) return;

        if (slot.Pending is { IsCompleted: true } finished)
        {
            slot.Pending = null;
            ReadResult result = finished.Result;
            if (result.Succeeded)
            {
                slot.Metadata = result.Metadata;
                slot.Error = null;
                slot.Succeeded = true;
                slot.ConsecutiveFailures = 0;
                if (includeSerialNumbers)
                {
                    _public.Metadata = RemoveSerialNumbers(result.Metadata);
                    _public.Error = null;
                    _public.Succeeded = true;
                }

                return;
            }

            slot.Error = result.Error;
            slot.ConsecutiveFailures++;
            slot.NextAttempt = DateTime.UtcNow + RetryDelay(slot.ConsecutiveFailures);
        }

        if (slot.Pending != null || DateTime.UtcNow < slot.NextAttempt) return;
        slot.Pending = Task.Run(() => TryRead(includeSerialNumbers));
    }

    private static ReadResult TryRead(bool includeSerialNumbers)
    {
        try
        {
            using ComApartmentScope apartment = ComApartmentScope.Enter(Ole32.COINIT_MULTITHREADED);
            if (!apartment.IsUsable)
            {
                return new ReadResult(
                    Succeeded: false,
                    PhysicalMemoryHardwareMetadata.Empty,
                    $"CoInitializeEx(MTA) failed ({WmiNative.FormatHResult(apartment.HResult)}).");
            }

            if (!WmiConnection.TryConnect(WmiNamespace, out WmiConnection? connection, out string? error))
                return new ReadResult(Succeeded: false, PhysicalMemoryHardwareMetadata.Empty, error);

            using (connection)
            {
                List<PhysicalMemoryModuleData> modules = [];
                connection.Query(
                    includeSerialNumbers ? PrivateModuleQuery : PublicModuleQuery,
                    memoryObject => modules.Add(ReadModule(memoryObject, includeSerialNumbers)));
                modules.RemoveAll(static module => module.CapacityBytes == 0
                                                   && string.IsNullOrWhiteSpace(module.BankLabel)
                                                   && string.IsNullOrWhiteSpace(module.PartNumber));
                modules.Sort(CompareModules);
                int totalSlotCount = ReadTotalSlotCount(connection);
                return new ReadResult(Succeeded: true, BuildMetadata(modules, totalSlotCount), Error: null);
            }
        }
        catch (Exception exception)
        {
            return new ReadResult(Succeeded: false, PhysicalMemoryHardwareMetadata.Empty, exception.Message);
        }
    }

    private sealed class MetadataSlot
    {
        public PhysicalMemoryHardwareMetadata Metadata = PhysicalMemoryHardwareMetadata.Empty;
        public string? Error;
        public bool Succeeded;
        public int ConsecutiveFailures;
        public DateTime NextAttempt = DateTime.MinValue;
        public Task<ReadResult>? Pending;
    }

    private readonly record struct ReadResult(bool Succeeded, PhysicalMemoryHardwareMetadata Metadata, string? Error);

    private static PhysicalMemoryModuleData ReadModule(
        IWbemClassObject memoryObject,
        bool includeSerialNumbers)
    {
        _ = WmiConnection.TryGetString(memoryObject, name: "BankLabel", out string bankLabel);
        _ = WmiConnection.TryGetUInt64(memoryObject, name: "Capacity", out ulong capacityBytes);
        _ = WmiConnection.TryGetUInt64(
            memoryObject,
            name: "ConfiguredClockSpeed",
            out ulong configuredSpeedMegatransfersPerSecond);
        _ = WmiConnection.TryGetUInt64(memoryObject, name: "Speed", out ulong fallbackSpeedMegatransfersPerSecond);
        _ = WmiConnection.TryGetUInt64(memoryObject, name: "FormFactor", out ulong formFactor);
        _ = WmiConnection.TryGetString(memoryObject, name: "PartNumber", out string partNumber);
        string serialNumber = string.Empty;
        if (includeSerialNumbers)
            _ = WmiConnection.TryGetString(memoryObject, name: "SerialNumber", out serialNumber);

        ulong speedMegatransfersPerSecond = configuredSpeedMegatransfersPerSecond > 0
            ? configuredSpeedMegatransfersPerSecond
            : fallbackSpeedMegatransfersPerSecond;
        return new PhysicalMemoryModuleData(
            NormalizeText(bankLabel),
            capacityBytes,
            speedMegatransfersPerSecond,
            formFactor <= ushort.MaxValue ? (ushort)formFactor : (ushort)0,
            NormalizeText(partNumber),
            NormalizeText(serialNumber));
    }

    private static int ReadTotalSlotCount(WmiConnection connection)
    {
        ulong totalSlots = 0;
        connection.Query(
            SlotQuery,
            memoryArray =>
            {
                if (!WmiConnection.TryGetUInt64(memoryArray, name: "MemoryDevices", out ulong memoryDevices)) return;
                totalSlots = SaturatingAdd(totalSlots, memoryDevices);
            });
        return totalSlots > int.MaxValue ? int.MaxValue : (int)totalSlots;
    }

    private static PhysicalMemoryHardwareMetadata BuildMetadata(
        IReadOnlyList<PhysicalMemoryModuleData> modules,
        int totalSlotCount)
    {
        PhysicalMemoryModuleSnapshot[] snapshots = new PhysicalMemoryModuleSnapshot[modules.Count];
        ulong commonSpeed = 0;
        string? commonFormFactor = null;
        bool hasMixedFormFactors = false;
        for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
        {
            PhysicalMemoryModuleData module = modules[moduleIndex];
            snapshots[moduleIndex] = new PhysicalMemoryModuleSnapshot(
                module.BankLabel,
                module.CapacityBytes,
                module.PartNumber,
                module.SerialNumber);
            if (module.SpeedMegatransfersPerSecond > 0)
            {
                commonSpeed = commonSpeed == 0
                    ? module.SpeedMegatransfersPerSecond
                    : Math.Min(commonSpeed, module.SpeedMegatransfersPerSecond);
            }

            string formFactor = FormatFormFactor(module.FormFactor);
            if (string.Equals(formFactor, b: "Unknown", StringComparison.Ordinal)) continue;
            if (commonFormFactor == null)
            {
                commonFormFactor = formFactor;
                continue;
            }

            if (!string.Equals(commonFormFactor, formFactor, StringComparison.Ordinal))
                hasMixedFormFactors = true;
        }

        int usedSlotCount = modules.Count;
        return new PhysicalMemoryHardwareMetadata(
            commonSpeed,
            usedSlotCount,
            Math.Max(usedSlotCount, totalSlotCount),
            hasMixedFormFactors ? "Mixed" : commonFormFactor ?? "Unknown",
            snapshots);
    }

    private static PhysicalMemoryHardwareMetadata RemoveSerialNumbers(
        PhysicalMemoryHardwareMetadata metadata)
    {
        ReadOnlySpan<PhysicalMemoryModuleSnapshot> modules = metadata.Modules.Span;
        PhysicalMemoryModuleSnapshot[] publicModules = new PhysicalMemoryModuleSnapshot[modules.Length];
        for (int moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
        {
            PhysicalMemoryModuleSnapshot module = modules[moduleIndex];
            publicModules[moduleIndex] = module with { SerialNumber = string.Empty };
        }

        return metadata with { Modules = publicModules };
    }

    private static int CompareModules(PhysicalMemoryModuleData left, PhysicalMemoryModuleData right)
    {
        int bankComparison = StringComparer.OrdinalIgnoreCase.Compare(left.BankLabel, right.BankLabel);
        if (bankComparison != 0) return bankComparison;

        int capacityComparison = left.CapacityBytes.CompareTo(right.CapacityBytes);
        if (capacityComparison != 0) return capacityComparison;
        return StringComparer.OrdinalIgnoreCase.Compare(left.PartNumber, right.PartNumber);
    }

    private static string FormatFormFactor(ushort formFactor) => formFactor switch
    {
        2 => "SIP",
        3 => "DIP",
        4 => "ZIP",
        5 => "SOJ",
        7 => "SIMM",
        8 => "DIMM",
        9 => "TSOP",
        10 => "PGA",
        11 => "RIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        14 => "SMD",
        15 => "SSMP",
        16 => "QFP",
        17 => "TQFP",
        18 => "SOIC",
        19 => "LCC",
        20 => "PLCC",
        21 => "BGA",
        22 => "FPBGA",
        23 => "LGA",
        _ => "Unknown"
    };

    private static string NormalizeText(string value) => value.Trim().TrimEnd('\0').Trim();

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        left > ulong.MaxValue - right ? ulong.MaxValue : left + right;

    /// <summary>Returns how long to wait before retrying after the given number of consecutive failed reads.</summary>
    internal static TimeSpan RetryDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0) return TimeSpan.Zero;
        int doublings = Math.Min(consecutiveFailures - 1, 16);
        long delayMilliseconds = Math.Min(
            InitialRetryDelayMilliseconds << doublings,
            MaximumRetryDelayMilliseconds);
        return TimeSpan.FromMilliseconds(delayMilliseconds);
    }

    private sealed record PhysicalMemoryModuleData(
        string BankLabel,
        ulong CapacityBytes,
        ulong SpeedMegatransfersPerSecond,
        ushort FormFactor,
        string PartNumber,
        string SerialNumber);
}

internal readonly record struct PhysicalMemoryHardwareMetadata(
    ulong SpeedMegatransfersPerSecond,
    int UsedSlotCount,
    int TotalSlotCount,
    string FormFactor,
    ReadOnlyMemory<PhysicalMemoryModuleSnapshot> Modules)
{
    public static PhysicalMemoryHardwareMetadata Empty { get; } = new(
        SpeedMegatransfersPerSecond: 0,
        UsedSlotCount: 0,
        TotalSlotCount: 0,
        FormFactor: "Unknown",
        ReadOnlyMemory<PhysicalMemoryModuleSnapshot>.Empty);
}
