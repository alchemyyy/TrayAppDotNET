using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads the memory latency and bandwidth the firmware declares in the ACPI HMAT.</summary>
/// <remarks>
/// The Heterogeneous Memory Attribute Table (ACPI 6.2 and later) is optional, and many desktop boards omit it. Its
/// values are the firmware's nominal figures, not measurements. Only the entries for the memory itself are read;
/// memory-side cache levels are skipped. <c>GetSystemFirmwareTable</c> needs no administrator rights.
/// </remarks>
internal static class MemoryFirmwarePerformanceReader
{
    // GetSystemFirmwareTable takes the provider and table signatures as DWORDs whose bytes spell the name
    private const uint ACPIProvider = 0x41435049;
    private const uint HMATSignature = 0x54414D48;

    // 36-byte ACPI header followed by 4 reserved bytes
    private const int TableHeaderLength = 40;
    private const int StructureHeaderLength = 8;
    private const ushort LocalityStructureType = 1;
    private const int LocalityHeaderLength = 32;
    private const int MemoryHierarchyMask = 0x0F;
    private const int DataTypeCount = 6;
    private const ushort UnreachableEntry = ushort.MaxValue;

    /// <summary>Reads and parses this machine's HMAT, or returns <see cref="MemoryFirmwarePerformance.None"/>.</summary>
    public static MemoryFirmwarePerformance Read()
    {
        try
        {
            uint size = GetSystemFirmwareTable(ACPIProvider, HMATSignature, buffer: null, bufferSize: 0);
            if (size == 0) return MemoryFirmwarePerformance.None;

            byte[] table = new byte[size];
            uint read = GetSystemFirmwareTable(ACPIProvider, HMATSignature, table, size);
            return read == size ? Parse(table) : MemoryFirmwarePerformance.None;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"MemoryFirmwarePerformanceReader: {exception.Message}");
            return MemoryFirmwarePerformance.None;
        }
    }

    /// <summary>Collects the memory-level latency and bandwidth entries of a raw HMAT.</summary>
    /// <remarks>
    /// Revision 1 tables (ACPI 6.2) count both quantities in tenths of a nanosecond or MB/s; revision 2 (ACPI 6.3 and
    /// later) counts latency in picoseconds and bandwidth in MB/s. Entries of 0 (not provided) and 0xFFFF
    /// (unreachable) are skipped. Several initiator and target domains produce a range.
    /// </remarks>
    internal static MemoryFirmwarePerformance Parse(ReadOnlySpan<byte> table)
    {
        if (table.Length < TableHeaderLength || !table[..4].SequenceEqual("HMAT"u8))
            return MemoryFirmwarePerformance.None;

        int tableLength = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(table[4..]), (uint)table.Length);
        byte revision = table[8];
        Span<ulong> minimums = stackalloc ulong[DataTypeCount];
        Span<ulong> maximums = stackalloc ulong[DataTypeCount];
        int offset = TableHeaderLength;
        while (offset + StructureHeaderLength <= tableLength)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(table[offset..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(table[(offset + 4)..]);
            if (length < StructureHeaderLength || length > tableLength - offset) break;

            if (type == LocalityStructureType && length >= LocalityHeaderLength)
                ReadLocalityStructure(table.Slice(offset, (int)length), revision, minimums, maximums);
            offset += (int)length;
        }

        return new MemoryFirmwarePerformance(
            new MemoryFirmwareMetricRange(minimums[0], maximums[0]),
            new MemoryFirmwareMetricRange(minimums[1], maximums[1]),
            new MemoryFirmwareMetricRange(minimums[2], maximums[2]),
            new MemoryFirmwareMetricRange(minimums[3], maximums[3]),
            new MemoryFirmwareMetricRange(minimums[4], maximums[4]),
            new MemoryFirmwareMetricRange(minimums[5], maximums[5]));
    }

    private static void ReadLocalityStructure(
        ReadOnlySpan<byte> structure,
        byte revision,
        Span<ulong> minimums,
        Span<ulong> maximums)
    {
        if ((structure[8] & MemoryHierarchyMask) != 0) return;

        int dataType = structure[9];
        if (dataType >= DataTypeCount) return;

        ulong initiatorCount = BinaryPrimitives.ReadUInt32LittleEndian(structure[12..]);
        ulong targetCount = BinaryPrimitives.ReadUInt32LittleEndian(structure[16..]);
        ulong baseUnit = BinaryPrimitives.ReadUInt64LittleEndian(structure[24..]);
        ulong entriesOffset = LocalityHeaderLength + 4 * (initiatorCount + targetCount);
        ulong entryCount = initiatorCount * targetCount;
        if (baseUnit == 0 || entriesOffset + 2 * entryCount > (ulong)structure.Length) return;

        bool isLatency = dataType <= 2;
        for (int entryIndex = 0; entryIndex < (int)entryCount; entryIndex++)
        {
            ushort entry = BinaryPrimitives.ReadUInt16LittleEndian(structure[((int)entriesOffset + 2 * entryIndex)..]);
            if (entry is 0 or UnreachableEntry) continue;
            if (!TryNormalize(entry, baseUnit, revision, isLatency, out ulong value) || value == 0) continue;

            minimums[dataType] = minimums[dataType] == 0 ? value : Math.Min(minimums[dataType], value);
            maximums[dataType] = Math.Max(maximums[dataType], value);
        }
    }

    // Latency is returned in picoseconds and bandwidth in MB/s
    private static bool TryNormalize(ushort entry, ulong baseUnit, byte revision, bool isLatency, out ulong value)
    {
        value = 0;
        if (baseUnit > ulong.MaxValue / entry) return false;

        ulong raw = entry * baseUnit;
        if (revision >= 2)
        {
            value = raw;
            return true;
        }

        // Revision 1 values are tenths of a nanosecond or of a MB/s
        if (!isLatency)
        {
            value = raw / 10;
            return true;
        }

        if (raw > ulong.MaxValue / 100) return false;
        value = raw * 100;
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(
        uint firmwareTableProviderSignature,
        uint firmwareTableID,
        byte[]? buffer,
        uint bufferSize);
}
