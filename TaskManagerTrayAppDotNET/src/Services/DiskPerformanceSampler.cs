using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Discovers Windows physical disks and samples them through IOCTL_DISK_PERFORMANCE.</summary>
internal sealed class DiskPerformanceSampler
{
    private const string PhysicalDrivePrefix = "PhysicalDrive";
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint IOCTLStorageGetDeviceNumber = 0x002D1080;
    private const uint IOCTLStorageQueryProperty = 0x002D1400;
    private const uint IOCTLDiskPerformance = 0x00070020;
    private const uint IOCTLDiskGetDriveGeometryEx = 0x000700A0;
    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceIDProperty = 2;
    private const int PropertyStandardQuery = 0;
    private const int StorageDescriptorBufferSize = 4_096;
    private const int StorageDescriptorHeaderSize = 8;
    private const int StorageDeviceIDDescriptorHeaderSize = 12;
    private const int StorageIdentifierHeaderSize = 16;
    private const int MaximumStorageDeviceIDDescriptorSize = 1_048_576;
    private const int StorageIDAssociationDevice = 0;
    private const int StorageIDTypeVendorSpecific = 0;
    private const int StorageIDTypeVendorID = 1;
    private const int StorageIDTypeEUI64 = 2;
    private const int StorageIDTypeNAA = 3;
    private const int StorageIDTypeMD5LogicalUnitIdentifier = 7;
    private const int StorageIDTypeSCSINameString = 8;
    private const int DriveGeometryBufferSize = 256;
    private const int DiskSizeOffset = 24;
    private const int InitialDeviceNameBufferLength = 32_768;
    private const int MaximumDeviceNameBufferLength = 1_048_576;
    private const int ErrorInsufficientBuffer = 122;
    private const double HundredNanosecondsPerSecond = TimeSpan.TicksPerSecond;
    private const double HundredNanosecondsPerMillisecond = TimeSpan.TicksPerMillisecond;

    private readonly Dictionary<string, DiskCounterState> _previousCounters =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeDeviceIDs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Discards kernel-counter baselines after sampling has been paused.</summary>
    internal void ResetCounterBaselines()
    {
        _previousCounters.Clear();
        _activeDeviceIDs.Clear();
    }

    /// <summary>Captures every exposed physical disk and merges any ready-volume metadata.</summary>
    public DiskPerformanceSnapshot[] Sample()
    {
        Dictionary<uint, DiskCandidateBuilder> disksByNumber = EnumeratePhysicalDisks();
        List<DiskPerformanceSnapshot> snapshots = new(disksByNumber.Count);
        HashSet<string> usedDeviceIDs = new(StringComparer.OrdinalIgnoreCase);
        _activeDeviceIDs.Clear();

        foreach (KeyValuePair<uint, DiskCandidateBuilder> pair in disksByNumber.OrderBy(static pair => pair.Key))
        {
            uint physicalDiskNumber = pair.Key;
            DiskCandidateBuilder candidate = pair.Value;
            string physicalDiskPath = string.Create(
                CultureInfo.InvariantCulture,
                $@"\\.\PhysicalDrive{physicalDiskNumber}");
            using SafeFileHandle diskHandle = OpenDevice(physicalDiskPath);

            DiskIdentity identity = diskHandle.IsInvalid
                ? DiskIdentity.Fallback(physicalDiskNumber)
                : ReadDiskIdentity(diskHandle, physicalDiskNumber);
            string deviceID = identity.DeviceID;
            if (!usedDeviceIDs.Add(deviceID))
            {
                deviceID = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{deviceID}:physical:{physicalDiskNumber}");
                usedDeviceIDs.Add(deviceID);
            }

            ulong capacityBytes = diskHandle.IsInvalid
                ? candidate.FormattedCapacityBytes
                : ReadDiskCapacityBytes(diskHandle, candidate.FormattedCapacityBytes);
            bool hasPerformanceSample = false;
            DiskPerformanceDelta delta = default;
            DISK_PERFORMANCE currentCounters = default;
            if (!diskHandle.IsInvalid && TryReadDiskPerformance(diskHandle, out currentCounters))
            {
                hasPerformanceSample = _previousCounters.TryGetValue(deviceID, out DiskCounterState previous)
                                       && TryCalculatePerformance(
                                           previous.Counters,
                                           currentCounters,
                                           out delta);
                _previousCounters[deviceID] = new DiskCounterState(currentCounters);
                _activeDeviceIDs.Add(deviceID);
            }

            snapshots.Add(new DiskPerformanceSnapshot(
                deviceID,
                PerformanceDeviceKind.Disk,
                checked((int)Math.Min(physicalDiskNumber, int.MaxValue)),
                identity.Name,
                candidate.GetVolumeNames(),
                identity.DeviceType,
                hasPerformanceSample,
                delta.ActiveTimePercent,
                delta.ReadBytesPerSecond,
                delta.WriteBytesPerSecond,
                delta.AverageResponseTimeMilliseconds,
                currentCounters.QueueDepth,
                capacityBytes,
                candidate.FormattedCapacityBytes,
                candidate.AvailableBytes));
        }

        RemoveMissingCounterStates();
        return [.. snapshots];
    }

    /// <summary>Calculates disk rates from two cumulative kernel counter snapshots.</summary>
    internal static bool TryCalculatePerformance(
        DiskPerformanceCounters previous,
        DiskPerformanceCounters current,
        out DiskPerformanceDelta delta)
    {
        delta = default;
        if (current.QueryTime <= previous.QueryTime
            || current.BytesRead < previous.BytesRead
            || current.BytesWritten < previous.BytesWritten
            || current.ReadTime < previous.ReadTime
            || current.WriteTime < previous.WriteTime
            || current.IdleTime < previous.IdleTime
            || current.ReadCount < previous.ReadCount
            || current.WriteCount < previous.WriteCount)
        {
            return false;
        }

        long queryTimeDelta = current.QueryTime - previous.QueryTime;
        long idleTimeDelta = Math.Clamp(current.IdleTime - previous.IdleTime, 0, queryTimeDelta);
        double elapsedSeconds = queryTimeDelta / HundredNanosecondsPerSecond;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return false;

        double activeTimePercent = (1.0 - idleTimeDelta / (double)queryTimeDelta) * 100.0;
        double readBytesPerSecond = (current.BytesRead - previous.BytesRead) / elapsedSeconds;
        double writeBytesPerSecond = (current.BytesWritten - previous.BytesWritten) / elapsedSeconds;
        ulong operationCount = (ulong)(current.ReadCount - previous.ReadCount)
                               + current.WriteCount - previous.WriteCount;
        double averageResponseTimeMilliseconds = operationCount == 0
            ? 0
            : ((current.ReadTime - previous.ReadTime)
               + (current.WriteTime - previous.WriteTime))
              / HundredNanosecondsPerMillisecond
              / operationCount;
        if (!double.IsFinite(activeTimePercent)
            || !double.IsFinite(readBytesPerSecond)
            || !double.IsFinite(writeBytesPerSecond)
            || !double.IsFinite(averageResponseTimeMilliseconds))
        {
            return false;
        }

        delta = new DiskPerformanceDelta(
            Math.Clamp(activeTimePercent, 0, 100),
            Math.Max(0, readBytesPerSecond),
            Math.Max(0, writeBytesPerSecond),
            Math.Max(0, averageResponseTimeMilliseconds));
        return true;
    }

    private static Dictionary<uint, DiskCandidateBuilder> EnumeratePhysicalDisks()
    {
        Dictionary<uint, DiskCandidateBuilder> disksByNumber = [];
        uint[] physicalDiskNumbers = EnumeratePhysicalDiskNumbers();
        for (int diskIndex = 0; diskIndex < physicalDiskNumbers.Length; diskIndex++)
            disksByNumber.Add(physicalDiskNumbers[diskIndex], new DiskCandidateBuilder());

        AddReadyVolumeMetadata(disksByNumber);
        return disksByNumber;
    }

    /// <summary>Enumerates PhysicalDrive DOS-device names without probing arbitrary indices.</summary>
    internal static uint[] EnumeratePhysicalDiskNumbers()
    {
        int bufferLength = InitialDeviceNameBufferLength;
        while (bufferLength <= MaximumDeviceNameBufferLength)
        {
            char[] deviceNames = new char[bufferLength];
            uint characterCount = QueryDosDeviceW(null, deviceNames, (uint)deviceNames.Length);
            if (characterCount > 0)
            {
                int validLength = Math.Min(deviceNames.Length, checked((int)characterCount));
                return ParsePhysicalDiskNumbers(deviceNames.AsSpan(0, validLength));
            }

            if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer
                || bufferLength == MaximumDeviceNameBufferLength)
            {
                return [];
            }

            bufferLength = Math.Min(
                checked(bufferLength * 2),
                MaximumDeviceNameBufferLength);
        }

        return [];
    }

    /// <summary>Parses, de-duplicates, and sorts PhysicalDrive names from a DOS-device multi-string.</summary>
    internal static uint[] ParsePhysicalDiskNumbers(ReadOnlySpan<char> deviceNames)
    {
        HashSet<uint> physicalDiskNumbers = [];
        int position = 0;
        while (position < deviceNames.Length)
        {
            ReadOnlySpan<char> remainingNames = deviceNames[position..];
            int terminatorOffset = remainingNames.IndexOf('\0');
            int nameLength = terminatorOffset >= 0 ? terminatorOffset : remainingNames.Length;
            if (nameLength == 0) break;

            ReadOnlySpan<char> deviceName = remainingNames[..nameLength];
            if (deviceName.StartsWith(PhysicalDrivePrefix, StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(
                    deviceName[PhysicalDrivePrefix.Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out uint physicalDiskNumber))
            {
                physicalDiskNumbers.Add(physicalDiskNumber);
            }

            position += nameLength + 1;
        }

        uint[] result = new uint[physicalDiskNumbers.Count];
        physicalDiskNumbers.CopyTo(result);
        Array.Sort(result);
        return result;
    }

    private static void AddReadyVolumeMetadata(
        Dictionary<uint, DiskCandidateBuilder> disksByNumber)
    {
        DriveInfo[] drives = DriveInfo.GetDrives();
        for (int driveIndex = 0; driveIndex < drives.Length; driveIndex++)
        {
            DriveInfo drive = drives[driveIndex];
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                    continue;
                if (!TryCreateVolumeDevicePath(drive.Name, out string volumePath)) continue;

                using SafeFileHandle volumeHandle = OpenDevice(volumePath);
                if (volumeHandle.IsInvalid
                    || !TryReadStorageDeviceNumber(volumeHandle, out STORAGE_DEVICE_NUMBER deviceNumber))
                {
                    continue;
                }

                if (!disksByNumber.TryGetValue(deviceNumber.DeviceNumber, out DiskCandidateBuilder? candidate))
                {
                    candidate = new DiskCandidateBuilder();
                    disksByNumber.Add(deviceNumber.DeviceNumber, candidate);
                }

                candidate.AddVolume(
                    drive.Name,
                    ToUnsigned(drive.TotalSize),
                    ToUnsigned(drive.AvailableFreeSpace));
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException)
            {
                // A volume can be removed or become unavailable during enumeration
                TADNLog.LogDebug(
                    $"DiskPerformanceSampler skipped volume '{drive.Name}': {exception.Message}");
            }
        }

    }

    private static bool TryCreateVolumeDevicePath(string driveName, out string devicePath)
    {
        string root = driveName.Trim();
        if (root.Length >= 2 && root[1] == ':')
        {
            devicePath = @"\\.\" + root[..2];
            return true;
        }

        devicePath = string.Empty;
        return false;
    }

    private static SafeFileHandle OpenDevice(string path) =>
        CreateFileW(
            path,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

    private static bool TryReadStorageDeviceNumber(
        SafeFileHandle handle,
        out STORAGE_DEVICE_NUMBER deviceNumber) =>
        DeviceIoControlStorageDeviceNumber(
            handle,
            IOCTLStorageGetDeviceNumber,
            IntPtr.Zero,
            0,
            out deviceNumber,
            (uint)Marshal.SizeOf<STORAGE_DEVICE_NUMBER>(),
            out uint bytesReturned,
            IntPtr.Zero)
        && bytesReturned >= Marshal.SizeOf<STORAGE_DEVICE_NUMBER>();

    private static DiskIdentity ReadDiskIdentity(SafeFileHandle handle, uint physicalDiskNumber)
    {
        byte[] query = new byte[12];
        BitConverter.GetBytes(StorageDeviceProperty).CopyTo(query, 0);
        BitConverter.GetBytes(PropertyStandardQuery).CopyTo(query, sizeof(int));
        byte[] descriptor = new byte[StorageDescriptorBufferSize];
        bool hasDeviceDescriptor = DeviceIoControlByteBuffers(
                handle,
                IOCTLStorageQueryProperty,
                query,
                (uint)query.Length,
                descriptor,
                (uint)descriptor.Length,
                out uint bytesReturned,
                IntPtr.Zero)
            && bytesReturned >= 36;

        string vendor = string.Empty;
        string product = string.Empty;
        string serial = string.Empty;
        string busName = "Disk";
        if (hasDeviceDescriptor)
        {
            vendor = ReadDescriptorString(descriptor, bytesReturned, 12);
            product = ReadDescriptorString(descriptor, bytesReturned, 16);
            serial = ReadDescriptorString(descriptor, bytesReturned, 24);
            busName = ResolveBusType(BitConverter.ToInt32(descriptor, 28));
        }

        string name = string.Join(
            " ",
            new[] { vendor, product }.Where(static value => value.Length > 0));
        if (name.Length == 0)
            name = string.Create(CultureInfo.InvariantCulture, $"Disk {physicalDiskNumber}");

        string page83DeviceID = ReadPage83DeviceID(handle);
        string deviceID;
        if (page83DeviceID.Length > 0)
        {
            deviceID = page83DeviceID;
        }
        else if (serial.Length > 0)
        {
            deviceID = "disk:" + NormalizeIdentity(busName) + ":" + NormalizeIdentity(serial);
        }
        else
        {
            deviceID = string.Create(
                CultureInfo.InvariantCulture,
                $"disk:physical:{physicalDiskNumber}");
        }

        return new DiskIdentity(deviceID, name, busName);
    }

    private static string ReadPage83DeviceID(SafeFileHandle handle)
    {
        byte[] query = new byte[12];
        BitConverter.GetBytes(StorageDeviceIDProperty).CopyTo(query, 0);
        BitConverter.GetBytes(PropertyStandardQuery).CopyTo(query, sizeof(int));
        byte[] header = new byte[StorageDescriptorHeaderSize];
        if (!DeviceIoControlByteBuffers(
                handle,
                IOCTLStorageQueryProperty,
                query,
                (uint)query.Length,
                header,
                (uint)header.Length,
                out uint headerBytesReturned,
                IntPtr.Zero)
            || headerBytesReturned < StorageDescriptorHeaderSize)
        {
            return string.Empty;
        }

        uint descriptorSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(sizeof(uint)));
        if (descriptorSize < StorageDeviceIDDescriptorHeaderSize
            || descriptorSize > MaximumStorageDeviceIDDescriptorSize)
        {
            return string.Empty;
        }

        byte[] descriptor = new byte[descriptorSize];
        if (!DeviceIoControlByteBuffers(
                handle,
                IOCTLStorageQueryProperty,
                query,
                (uint)query.Length,
                descriptor,
                (uint)descriptor.Length,
                out uint bytesReturned,
                IntPtr.Zero))
        {
            return string.Empty;
        }

        int validLength = Math.Min(descriptor.Length, checked((int)bytesReturned));
        return TryCreatePage83DeviceID(descriptor.AsSpan(0, validLength), out string deviceID)
            ? deviceID
            : string.Empty;
    }

    /// <summary>Selects a deterministic device-associated identifier from a VPD page 0x83 descriptor.</summary>
    internal static bool TryCreatePage83DeviceID(
        ReadOnlySpan<byte> descriptor,
        out string deviceID)
    {
        deviceID = string.Empty;
        if (descriptor.Length < StorageDeviceIDDescriptorHeaderSize) return false;

        uint declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[sizeof(uint)..]);
        if (declaredSize < StorageDeviceIDDescriptorHeaderSize
            || declaredSize > descriptor.Length)
        {
            return false;
        }

        uint identifierCount = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[(sizeof(uint) * 2)..]);
        int validLength = checked((int)declaredSize);
        int identifierOffset = StorageDeviceIDDescriptorHeaderSize;
        Page83IdentifierCandidate bestCandidate = default;
        bool hasBestCandidate = false;
        for (uint identifierIndex = 0; identifierIndex < identifierCount; identifierIndex++)
        {
            if (identifierOffset > validLength - StorageIdentifierHeaderSize) return false;

            ReadOnlySpan<byte> identifierHeader = descriptor.Slice(
                identifierOffset,
                StorageIdentifierHeaderSize);
            int codeSet = BinaryPrimitives.ReadInt32LittleEndian(identifierHeader);
            int identifierType = BinaryPrimitives.ReadInt32LittleEndian(identifierHeader[sizeof(int)..]);
            ushort identifierSize = BinaryPrimitives.ReadUInt16LittleEndian(identifierHeader[(sizeof(int) * 2)..]);
            ushort nextOffset = BinaryPrimitives.ReadUInt16LittleEndian(
                identifierHeader[(sizeof(int) * 2 + sizeof(ushort))..]);
            int association = BinaryPrimitives.ReadInt32LittleEndian(identifierHeader[12..]);
            int identifierEnd = checked(identifierOffset + StorageIdentifierHeaderSize + identifierSize);
            if (identifierEnd > validLength) return false;

            int priority = GetPage83IdentifierPriority(identifierType);
            ReadOnlySpan<byte> identifier = descriptor.Slice(
                identifierOffset + StorageIdentifierHeaderSize,
                identifierSize);
            if (association == StorageIDAssociationDevice
                && priority < int.MaxValue
                && ContainsNonzeroByte(identifier))
            {
                string hexadecimalIdentifier = Convert.ToHexString(identifier).ToLowerInvariant();
                Page83IdentifierCandidate candidate = new(
                    priority,
                    identifierType,
                    codeSet,
                    hexadecimalIdentifier);
                if (!hasBestCandidate || candidate.CompareTo(bestCandidate) < 0)
                {
                    bestCandidate = candidate;
                    hasBestCandidate = true;
                }
            }

            if (nextOffset == 0)
            {
                if (identifierIndex + 1 < identifierCount) return false;
                break;
            }

            if (nextOffset < StorageIdentifierHeaderSize + identifierSize
                || nextOffset > validLength - identifierOffset)
            {
                return false;
            }
            identifierOffset += nextOffset;
        }

        if (!hasBestCandidate) return false;
        deviceID = string.Create(
            CultureInfo.InvariantCulture,
            $"disk:vpd83:{bestCandidate.IdentifierType}:{bestCandidate.CodeSet}:{bestCandidate.HexadecimalIdentifier}");
        return true;
    }

    private static int GetPage83IdentifierPriority(int identifierType) => identifierType switch
    {
        StorageIDTypeNAA => 0,
        StorageIDTypeEUI64 => 1,
        StorageIDTypeSCSINameString => 2,
        StorageIDTypeMD5LogicalUnitIdentifier => 3,
        StorageIDTypeVendorID => 4,
        StorageIDTypeVendorSpecific => 5,
        _ => int.MaxValue
    };

    private static bool ContainsNonzeroByte(ReadOnlySpan<byte> value)
    {
        for (int byteIndex = 0; byteIndex < value.Length; byteIndex++)
        {
            if (value[byteIndex] != 0) return true;
        }
        return false;
    }

    private static string ReadDescriptorString(byte[] descriptor, uint validLength, int offsetFieldOffset)
    {
        uint stringOffset = BitConverter.ToUInt32(descriptor, offsetFieldOffset);
        if (stringOffset == 0 || stringOffset >= validLength || stringOffset >= descriptor.Length)
            return string.Empty;

        int maximumLength = (int)Math.Min(validLength, (uint)descriptor.Length);
        int end = (int)stringOffset;
        while (end < maximumLength && descriptor[end] != 0)
            end++;
        return Encoding.ASCII.GetString(descriptor, (int)stringOffset, end - (int)stringOffset).Trim();
    }

    private static string NormalizeIdentity(string value)
    {
        StringBuilder normalized = new(value.Length);
        for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
        {
            char character = char.ToLowerInvariant(value[characterIndex]);
            normalized.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '_');
        }

        return normalized.ToString().Trim('_');
    }

    private static string ResolveBusType(int busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE 1394",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "File-backed virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Disk"
    };

    private static ulong ReadDiskCapacityBytes(SafeFileHandle handle, ulong fallback)
    {
        byte[] geometry = new byte[DriveGeometryBufferSize];
        if (!DeviceIoControlOutputBuffer(
                handle,
                IOCTLDiskGetDriveGeometryEx,
                IntPtr.Zero,
                0,
                geometry,
                (uint)geometry.Length,
                out uint bytesReturned,
                IntPtr.Zero)
            || bytesReturned < DiskSizeOffset + sizeof(long))
        {
            return fallback;
        }

        return ToUnsigned(BitConverter.ToInt64(geometry, DiskSizeOffset));
    }

    private static bool TryReadDiskPerformance(
        SafeFileHandle handle,
        out DISK_PERFORMANCE counters) =>
        DeviceIoControlDiskPerformance(
            handle,
            IOCTLDiskPerformance,
            IntPtr.Zero,
            0,
            out counters,
            (uint)Marshal.SizeOf<DISK_PERFORMANCE>(),
            out uint bytesReturned,
            IntPtr.Zero)
        && bytesReturned >= Marshal.SizeOf<DISK_PERFORMANCE>();

    private void RemoveMissingCounterStates()
    {
        List<string> missingDeviceIDs = [];
        foreach (string deviceID in _previousCounters.Keys)
        {
            if (!_activeDeviceIDs.Contains(deviceID))
                missingDeviceIDs.Add(deviceID);
        }

        for (int missingIndex = 0; missingIndex < missingDeviceIDs.Count; missingIndex++)
            _previousCounters.Remove(missingDeviceIDs[missingIndex]);
    }

    private static ulong ToUnsigned(long value) => value > 0 ? (ulong)value : 0;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(
        string? deviceName,
        [Out] char[] targetPath,
        uint maximumCharacterCount);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlStorageDeviceNumber(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        uint inputBufferSize,
        out STORAGE_DEVICE_NUMBER outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlDiskPerformance(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        uint inputBufferSize,
        out DISK_PERFORMANCE outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlByteBuffers(
        SafeFileHandle device,
        uint controlCode,
        byte[] inputBuffer,
        uint inputBufferSize,
        byte[] outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlOutputBuffer(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        uint inputBufferSize,
        byte[] outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_DEVICE_NUMBER
    {
        public uint DeviceType;
        public uint DeviceNumber;
        public uint PartitionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DISK_PERFORMANCE
    {
        public long BytesRead;
        public long BytesWritten;
        public long ReadTime;
        public long WriteTime;
        public long IdleTime;
        public uint ReadCount;
        public uint WriteCount;
        public uint QueueDepth;
        public uint SplitCount;
        public long QueryTime;
        public uint StorageDeviceNumber;
        public fixed char StorageManagerName[8];

        public static implicit operator DiskPerformanceCounters(DISK_PERFORMANCE value) => new(
            value.BytesRead,
            value.BytesWritten,
            value.ReadTime,
            value.WriteTime,
            value.IdleTime,
            value.ReadCount,
            value.WriteCount,
            value.QueryTime);
    }

    private sealed class DiskCandidateBuilder
    {
        private readonly List<string> _volumeNames = [];

        public ulong FormattedCapacityBytes { get; private set; }
        public ulong AvailableBytes { get; private set; }

        public void AddVolume(string volumeName, ulong formattedCapacityBytes, ulong availableBytes)
        {
            _volumeNames.Add(volumeName.TrimEnd(Path.DirectorySeparatorChar));
            FormattedCapacityBytes = SaturatingAdd(FormattedCapacityBytes, formattedCapacityBytes);
            AvailableBytes = SaturatingAdd(AvailableBytes, availableBytes);
        }

        public string GetVolumeNames() => string.Join(", ", _volumeNames);

        private static ulong SaturatingAdd(ulong left, ulong right) =>
            left > ulong.MaxValue - right ? ulong.MaxValue : left + right;
    }

    private readonly record struct Page83IdentifierCandidate(
        int Priority,
        int IdentifierType,
        int CodeSet,
        string HexadecimalIdentifier)
    {
        public int CompareTo(Page83IdentifierCandidate other)
        {
            int comparison = Priority.CompareTo(other.Priority);
            if (comparison != 0) return comparison;
            comparison = IdentifierType.CompareTo(other.IdentifierType);
            if (comparison != 0) return comparison;
            comparison = CodeSet.CompareTo(other.CodeSet);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(HexadecimalIdentifier, other.HexadecimalIdentifier);
        }
    }

    private readonly record struct DiskIdentity(string DeviceID, string Name, string DeviceType)
    {
        public static DiskIdentity Fallback(uint physicalDiskNumber) => new(
            string.Create(CultureInfo.InvariantCulture, $"disk:physical:{physicalDiskNumber}"),
            string.Create(CultureInfo.InvariantCulture, $"Disk {physicalDiskNumber}"),
            "Disk");
    }

    private readonly record struct DiskCounterState(DiskPerformanceCounters Counters);
}

internal readonly record struct DiskPerformanceCounters(
    long BytesRead,
    long BytesWritten,
    long ReadTime,
    long WriteTime,
    long IdleTime,
    uint ReadCount,
    uint WriteCount,
    long QueryTime);

internal readonly record struct DiskPerformanceDelta(
    double ActiveTimePercent,
    double ReadBytesPerSecond,
    double WriteBytesPerSecond,
    double AverageResponseTimeMilliseconds);
