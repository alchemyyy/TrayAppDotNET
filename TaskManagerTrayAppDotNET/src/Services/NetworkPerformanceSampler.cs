using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Samples connected hardware network interfaces through the Windows IP Helper table.</summary>
internal sealed unsafe class NetworkPerformanceSampler
{
    private const uint NoError = 0;
    private const uint IfOperStatusUp = 1;
    private const uint IfTypeSoftwareLoopback = 24;
    private const uint IfTypeTunnel = 131;
    private const byte HardwareInterfaceFlag = 1 << 0;
    private const byte FilterInterfaceFlag = 1 << 1;
    private const byte ConnectorPresentFlag = 1 << 2;
    private const byte NotMediaConnectedFlag = 1 << 4;
    private const byte EndPointInterfaceFlag = 1 << 7;
    private const int MaximumInterfaceCount = 16_384;
    private const int InterfaceStringLength = 257;
    private const int PhysicalAddressLength = 32;

    private static readonly int InterfaceTableOffset =
        checked((int)Marshal.OffsetOf<MIB_IF_TABLE2_HEADER>(nameof(MIB_IF_TABLE2_HEADER.FirstRow)));

    private readonly Dictionary<string, NetworkCounterState> _previousCounters =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeDeviceIDs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NetworkCandidate> _candidates = [];

    /// <summary>Discards byte-counter baselines after sampling has been paused.</summary>
    internal void ResetCounterBaselines()
    {
        _previousCounters.Clear();
        _activeDeviceIDs.Clear();
    }

    /// <summary>Captures connected hardware interfaces and excludes filters, tunnels, and endpoints.</summary>
    public NetworkPerformanceSnapshot[] Sample(long timestamp)
    {
        _activeDeviceIDs.Clear();
        _candidates.Clear();

        uint status = GetIfTable2(out IntPtr table);
        if (status != NoError)
            throw new Win32Exception(checked((int)status));
        if (table == IntPtr.Zero)
            throw new InvalidOperationException("GetIfTable2 returned a null interface table.");

        try
        {
            uint nativeInterfaceCount = *(uint*)table;
            if (nativeInterfaceCount > MaximumInterfaceCount)
                throw new InvalidOperationException("GetIfTable2 returned an invalid interface count.");

            int interfaceCount = checked((int)nativeInterfaceCount);
            byte* firstRow = (byte*)table + InterfaceTableOffset;
            for (int interfaceIndex = 0; interfaceIndex < interfaceCount; interfaceIndex++)
            {
                MIB_IF_ROW2 row = *(MIB_IF_ROW2*)(firstRow + interfaceIndex * sizeof(MIB_IF_ROW2));
                byte flags = row.InterfaceAndOperStatusFlags;
                if (!IsMeaningfulInterface(
                        row.Type,
                        (flags & HardwareInterfaceFlag) != 0,
                        (flags & FilterInterfaceFlag) != 0,
                        (flags & ConnectorPresentFlag) != 0,
                        (flags & NotMediaConnectedFlag) != 0,
                        (flags & EndPointInterfaceFlag) != 0,
                        row.OperStatus == IfOperStatusUp))
                {
                    continue;
                }

                string deviceID = CreateDeviceID(row.InterfaceGuid, row.InterfaceLUID);
                long bytesReceived = ToSignedCounter(row.InOctets);
                long bytesSent = ToSignedCounter(row.OutOctets);
                double receiveBytesPerSecond = 0;
                double sendBytesPerSecond = 0;
                bool hasThroughputSample = _previousCounters.TryGetValue(
                                               deviceID,
                                               out NetworkCounterState previous)
                                           && TryCalculateThroughput(
                                               previous.BytesReceived,
                                               previous.BytesSent,
                                               previous.Timestamp,
                                               bytesReceived,
                                               bytesSent,
                                               timestamp,
                                               out receiveBytesPerSecond,
                                               out sendBytesPerSecond);
                if (!hasThroughputSample)
                {
                    receiveBytesPerSecond = 0;
                    sendBytesPerSecond = 0;
                }

                _previousCounters[deviceID] = new NetworkCounterState(
                    bytesReceived,
                    bytesSent,
                    timestamp);
                _activeDeviceIDs.Add(deviceID);

                string alias;
                string description;
                char* aliasPointer = row.Alias;
                alias = ReadFixedString(aliasPointer, InterfaceStringLength);
                char* descriptionPointer = row.Description;
                description = ReadFixedString(descriptionPointer, InterfaceStringLength);

                _candidates.Add(new NetworkCandidate(
                    deviceID,
                    ResolveDisplayName(alias, description),
                    description,
                    ((NetworkInterfaceType)row.Type).ToString(),
                    hasThroughputSample,
                    receiveBytesPerSecond,
                    sendBytesPerSecond,
                    ToSignedCounter(Math.Max(row.ReceiveLinkSpeed, row.TransmitLinkSpeed)),
                    bytesReceived,
                    bytesSent));
            }
        }
        finally
        {
            FreeMibTable(table);
        }

        RemoveMissingCounterStates();
        _candidates.Sort(static (left, right) =>
        {
            int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameComparison != 0
                ? nameComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.DeviceID, right.DeviceID);
        });

        NetworkPerformanceSnapshot[] snapshots = new NetworkPerformanceSnapshot[_candidates.Count];
        for (int candidateIndex = 0; candidateIndex < _candidates.Count; candidateIndex++)
        {
            NetworkCandidate candidate = _candidates[candidateIndex];
            snapshots[candidateIndex] = new NetworkPerformanceSnapshot(
                candidate.DeviceID,
                PerformanceDeviceKind.Network,
                candidateIndex,
                candidate.Name,
                candidate.Description,
                candidate.InterfaceType,
                true,
                candidate.HasThroughputSample,
                candidate.ReceiveBytesPerSecond,
                candidate.SendBytesPerSecond,
                candidate.LinkSpeedBitsPerSecond,
                candidate.TotalBytesReceived,
                candidate.TotalBytesSent);
        }

        return snapshots;
    }

    /// <summary>Decides whether structured IP Helper metadata represents a useful connected adapter.</summary>
    internal static bool IsMeaningfulInterface(
        uint interfaceType,
        bool isHardwareInterface,
        bool isFilterInterface,
        bool isConnectorPresent,
        bool isMediaDisconnected,
        bool isEndPointInterface,
        bool isOperational)
    {
        if (interfaceType is IfTypeSoftwareLoopback or IfTypeTunnel) return false;
        return isHardwareInterface
               && !isFilterInterface
               && isConnectorPresent
               && !isMediaDisconnected
               && !isEndPointInterface
               && isOperational;
    }

    /// <summary>Calculates byte rates while rejecting counter resets and invalid intervals.</summary>
    internal static bool TryCalculateThroughput(
        long previousBytesReceived,
        long previousBytesSent,
        long previousTimestamp,
        long currentBytesReceived,
        long currentBytesSent,
        long currentTimestamp,
        out double receiveBytesPerSecond,
        out double sendBytesPerSecond)
    {
        receiveBytesPerSecond = 0;
        sendBytesPerSecond = 0;
        if (previousBytesReceived < 0
            || previousBytesSent < 0
            || currentBytesReceived < previousBytesReceived
            || currentBytesSent < previousBytesSent
            || currentTimestamp <= previousTimestamp)
        {
            return false;
        }

        double elapsedSeconds = (currentTimestamp - previousTimestamp) / (double)Stopwatch.Frequency;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return false;

        receiveBytesPerSecond = (currentBytesReceived - previousBytesReceived) / elapsedSeconds;
        sendBytesPerSecond = (currentBytesSent - previousBytesSent) / elapsedSeconds;
        return double.IsFinite(receiveBytesPerSecond)
               && double.IsFinite(sendBytesPerSecond)
               && receiveBytesPerSecond >= 0
               && sendBytesPerSecond >= 0;
    }

    private static string CreateDeviceID(Guid interfaceGuid, ulong interfaceLUID) =>
        interfaceGuid != Guid.Empty
            ? "network:" + interfaceGuid.ToString("D").ToLowerInvariant()
            : string.Create(
                CultureInfo.InvariantCulture,
                $"network:luid:{interfaceLUID:x16}");

    private static string ResolveDisplayName(string alias, string description)
    {
        if (!string.IsNullOrWhiteSpace(alias)) return alias.Trim();
        if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        return "Network adapter";
    }

    private static string ReadFixedString(char* value, int maximumLength)
    {
        int length = 0;
        while (length < maximumLength && value[length] != '\0')
            length++;
        return length == 0 ? string.Empty : new string(value, 0, length).Trim();
    }

    private static long ToSignedCounter(ulong value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;

    private void RemoveMissingCounterStates()
    {
        if (_previousCounters.Count == _activeDeviceIDs.Count) return;

        List<string> missingDeviceIDs = [];
        foreach (string deviceID in _previousCounters.Keys)
        {
            if (!_activeDeviceIDs.Contains(deviceID))
                missingDeviceIDs.Add(deviceID);
        }

        for (int missingIndex = 0; missingIndex < missingDeviceIDs.Count; missingIndex++)
            _previousCounters.Remove(missingDeviceIDs[missingIndex]);
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetIfTable2(out IntPtr table);

    [DllImport("iphlpapi.dll")]
    private static extern void FreeMibTable(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_IF_TABLE2_HEADER
    {
        public uint InterfaceCount;
        public MIB_IF_ROW2 FirstRow;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct MIB_IF_ROW2
    {
        public ulong InterfaceLUID;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        public fixed char Alias[InterfaceStringLength];
        public fixed char Description[InterfaceStringLength];
        public uint PhysicalAddressByteCount;
        public fixed byte PhysicalAddress[PhysicalAddressLength];
        public fixed byte PermanentPhysicalAddress[PhysicalAddressLength];
        public uint MTU;
        public uint Type;
        public uint TunnelType;
        public uint MediaType;
        public uint PhysicalMediumType;
        public uint AccessType;
        public uint DirectionType;
        public byte InterfaceAndOperStatusFlags;
        public uint OperStatus;
        public uint AdminStatus;
        public uint MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUnicastPackets;
        public ulong InNonUnicastPackets;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtocols;
        public ulong InUnicastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUnicastPackets;
        public ulong OutNonUnicastPackets;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUnicastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQueueLength;
    }

    private readonly record struct NetworkCounterState(
        long BytesReceived,
        long BytesSent,
        long Timestamp);

    private readonly record struct NetworkCandidate(
        string DeviceID,
        string Name,
        string Description,
        string InterfaceType,
        bool HasThroughputSample,
        double ReceiveBytesPerSecond,
        double SendBytesPerSecond,
        long LinkSpeedBitsPerSecond,
        long TotalBytesReceived,
        long TotalBytesSent);
}
