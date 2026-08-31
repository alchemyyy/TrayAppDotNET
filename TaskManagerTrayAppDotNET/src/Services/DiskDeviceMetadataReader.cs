using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads physical-disk volume roles and media behavior through native storage APIs.</summary>
internal sealed class DiskDeviceMetadataReader
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint IOCTLStorageQueryProperty = 0x002D1400;
    private const uint IOCTLVolumeGetVolumeDiskExtents = 0x00560000;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int PropertyStandardQuery = 0;
    private const int SeekPenaltyValueOffset = sizeof(uint) * 2;
    private const int MinimumSeekPenaltyDescriptorSize = SeekPenaltyValueOffset + sizeof(byte);
    private const int StoragePropertyQuerySize = 12;
    private const int VolumeNameBufferLength = 1_024;
    private const int InitialPathBufferLength = 256;
    private const int MaximumPathBufferLength = 32_768;
    private const int InitialDosDeviceBufferLength = 32_768;
    private const int MaximumDosDeviceBufferLength = 1_048_576;
    private const int InitialExtentBufferLength = 256;
    private const int MaximumExtentBufferLength = 1_048_576;
    private const int VolumeExtentArrayOffset = 8;
    private const int DiskExtentSize = 24;
    private const int DiskExtentLengthOffset = 16;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorMoreData = 234;
    private const string ExtendedPathPrefix = @"\\?\";
    private const string NativeDosPathPrefix = @"\??\";
    private const string NativeDevicePathPrefix = @"\Device\";
    private const string SystemRootPrefix = @"\SystemRoot";

    /// <summary>Reads metadata for every currently exposed physical disk.</summary>
    public DiskDeviceMetadataSnapshot[] Read()
    {
        uint[] physicalDiskNumbers = DiskPerformanceSampler.EnumeratePhysicalDiskNumbers();
        return Read(physicalDiskNumbers);
    }

    /// <summary>Reads metadata while retaining explicitly supplied raw disks with no volumes.</summary>
    internal static DiskDeviceMetadataSnapshot[] Read(ReadOnlySpan<uint> physicalDiskNumbers)
    {
        Dictionary<uint, DiskMetadataBuilder> metadataByDiskNumber = [];
        for (int diskIndex = 0; diskIndex < physicalDiskNumbers.Length; diskIndex++)
        {
            uint physicalDiskNumber = physicalDiskNumbers[diskIndex];
            metadataByDiskNumber.TryAdd(
                physicalDiskNumber,
                new DiskMetadataBuilder(physicalDiskNumber));
        }

        List<string> volumeNames = EnumerateVolumeNames();
        Dictionary<string, string> volumeByNativeDevicePath =
            CreateNativeDeviceVolumeMap(volumeNames);
        string systemVolumeName = ReadSystemVolumeName(out bool hasSystemDiskData);
        HashSet<string> pageFileVolumeNames = ReadPageFileVolumeNames(
            volumeByNativeDevicePath,
            out bool hasPageFileData);
        bool mappedSystemVolume = false;
        HashSet<string> mappedPageFileVolumeNames = new(StringComparer.OrdinalIgnoreCase);
        for (int volumeIndex = 0; volumeIndex < volumeNames.Count; volumeIndex++)
        {
            string volumeName = volumeNames[volumeIndex];
            DiskVolumeRoleMapping roleMapping = AddVolumeMetadata(
                volumeName,
                systemVolumeName,
                pageFileVolumeNames,
                metadataByDiskNumber);
            mappedSystemVolume |= roleMapping.IsSystemVolume;
            if (roleMapping.HasPageFile)
                mappedPageFileVolumeNames.Add(volumeName);
        }

        hasSystemDiskData &= mappedSystemVolume;
        hasPageFileData &= mappedPageFileVolumeNames.SetEquals(pageFileVolumeNames);

        uint[] sortedDiskNumbers = new uint[metadataByDiskNumber.Count];
        metadataByDiskNumber.Keys.CopyTo(sortedDiskNumbers, index: 0);
        Array.Sort(sortedDiskNumbers);
        DiskDeviceMetadataSnapshot[] snapshots =
            new DiskDeviceMetadataSnapshot[sortedDiskNumbers.Length];
        for (int diskIndex = 0; diskIndex < sortedDiskNumbers.Length; diskIndex++)
        {
            uint physicalDiskNumber = sortedDiskNumbers[diskIndex];
            DiskMetadataBuilder builder = metadataByDiskNumber[physicalDiskNumber];
            snapshots[diskIndex] = builder.Build(
                ReadMediaKind(physicalDiskNumber),
                hasSystemDiskData,
                hasPageFileData);
        }

        return snapshots;
    }

    /// <summary>Parses the x64 VOLUME_DISK_EXTENTS layout with strict bounds checks.</summary>
    internal static bool TryParseVolumeDiskExtents(
        ReadOnlySpan<byte> descriptor,
        out DiskVolumeExtent[] extents)
    {
        extents = [];
        if (descriptor.Length < VolumeExtentArrayOffset) return false;

        uint extentCount = BinaryPrimitives.ReadUInt32LittleEndian(descriptor);
        if (extentCount is 0 or > int.MaxValue) return false;

        long requiredLength = VolumeExtentArrayOffset + (long)extentCount * DiskExtentSize;
        if (requiredLength > descriptor.Length) return false;

        DiskVolumeExtent[] parsedExtents = new DiskVolumeExtent[extentCount];
        for (int extentIndex = 0; extentIndex < parsedExtents.Length; extentIndex++)
        {
            int extentOffset = VolumeExtentArrayOffset + extentIndex * DiskExtentSize;
            uint physicalDiskNumber = BinaryPrimitives.ReadUInt32LittleEndian(
                descriptor[extentOffset..]);
            long extentLength = BinaryPrimitives.ReadInt64LittleEndian(
                descriptor[(extentOffset + DiskExtentLengthOffset)..]);
            if (extentLength <= 0) return false;

            parsedExtents[extentIndex] = new DiskVolumeExtent(
                physicalDiskNumber,
                (ulong)extentLength);
        }

        extents = parsedExtents;
        return true;
    }

    /// <summary>Splits logical volume bytes by physical extent using exact largest remainders.</summary>
    internal static DiskByteAllocation[] AllocateBytesByDisk(
        ulong byteCount,
        ReadOnlySpan<DiskVolumeExtent> extents)
    {
        Dictionary<uint, UInt128> weightByDiskNumber = [];
        UInt128 totalWeight = 0;
        for (int extentIndex = 0; extentIndex < extents.Length; extentIndex++)
        {
            DiskVolumeExtent extent = extents[extentIndex];
            if (extent.ExtentLengthBytes == 0) continue;

            UInt128 extentWeight = extent.ExtentLengthBytes;
            weightByDiskNumber.TryGetValue(
                extent.PhysicalDiskNumber,
                out UInt128 existingWeight);
            weightByDiskNumber[extent.PhysicalDiskNumber] = existingWeight + extentWeight;
            totalWeight += extentWeight;
        }

        if (weightByDiskNumber.Count == 0 || totalWeight == 0) return [];

        List<AllocationBuilder> allocations = new(weightByDiskNumber.Count);
        ulong allocatedBytes = 0;
        foreach (KeyValuePair<uint, UInt128> pair in weightByDiskNumber)
        {
            UInt128 product = byteCount * pair.Value;
            ulong quotient = (ulong)(product / totalWeight);
            UInt128 remainder = product % totalWeight;
            allocations.Add(new AllocationBuilder(pair.Key, quotient, remainder));
            allocatedBytes += quotient;
        }

        allocations.Sort(static (left, right) =>
        {
            int remainderComparison = right.Remainder.CompareTo(left.Remainder);
            return remainderComparison != 0
                ? remainderComparison
                : left.PhysicalDiskNumber.CompareTo(right.PhysicalDiskNumber);
        });
        ulong remainingBytes = byteCount - allocatedBytes;
        for (ulong remainderIndex = 0; remainderIndex < remainingBytes; remainderIndex++)
            allocations[checked((int)remainderIndex)].Bytes++;

        allocations.Sort(static (left, right) =>
            left.PhysicalDiskNumber.CompareTo(right.PhysicalDiskNumber));
        DiskByteAllocation[] result = new DiskByteAllocation[allocations.Count];
        for (int allocationIndex = 0; allocationIndex < allocations.Count; allocationIndex++)
        {
            AllocationBuilder allocation = allocations[allocationIndex];
            result[allocationIndex] = new DiskByteAllocation(
                allocation.PhysicalDiskNumber,
                allocation.Bytes);
        }

        return result;
    }

    /// <summary>Parses DEVICE_SEEK_PENALTY_DESCRIPTOR into a stable media category.</summary>
    internal static DiskMediaKind ParseSeekPenaltyDescriptor(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < MinimumSeekPenaltyDescriptorSize)
            return DiskMediaKind.Unknown;

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(descriptor);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[sizeof(uint)..]);
        if (version < MinimumSeekPenaltyDescriptorSize
            || size < MinimumSeekPenaltyDescriptorSize
            || size > descriptor.Length)
            return DiskMediaKind.Unknown;

        return descriptor[SeekPenaltyValueOffset] == 0
            ? DiskMediaKind.SolidState
            : DiskMediaKind.Rotational;
    }

    private static DiskVolumeRoleMapping AddVolumeMetadata(
        string normalizedVolumeName,
        string systemVolumeName,
        HashSet<string> pageFileVolumeNames,
        Dictionary<uint, DiskMetadataBuilder> metadataByDiskNumber)
    {
        using SafeFileHandle volumeHandle = OpenDevice(normalizedVolumeName);
        if (volumeHandle.IsInvalid
            || !TryReadVolumeDiskExtents(volumeHandle, out DiskVolumeExtent[] extents))
            return default;

        string volumePath = EnsureTrailingDirectorySeparator(normalizedVolumeName);
        bool hasVolumeData = GetDiskFreeSpaceExW(
            volumePath,
            out _,
            out ulong formattedCapacityBytes,
            out ulong availableBytes);
        string[] displayNames = ReadVolumeDisplayNames(volumePath);
        bool isSystemVolume = normalizedVolumeName.Equals(
            systemVolumeName,
            StringComparison.OrdinalIgnoreCase);
        bool hasPageFile = pageFileVolumeNames.Contains(normalizedVolumeName);
        DiskByteAllocation[] formattedAllocations = hasVolumeData
            ? AllocateBytesByDisk(formattedCapacityBytes, extents)
            : [];
        DiskByteAllocation[] availableAllocations = hasVolumeData
            ? AllocateBytesByDisk(availableBytes, extents)
            : [];

        HashSet<uint> affectedDiskNumbers = [];
        for (int extentIndex = 0; extentIndex < extents.Length; extentIndex++)
            affectedDiskNumbers.Add(extents[extentIndex].PhysicalDiskNumber);

        foreach (uint physicalDiskNumber in affectedDiskNumbers)
        {
            DiskMetadataBuilder builder = GetOrCreateBuilder(
                metadataByDiskNumber,
                physicalDiskNumber);
            builder.MarkVolumeRoles(isSystemVolume, hasPageFile);
            if (!hasVolumeData) continue;

            builder.AddVolumeData(
                displayNames,
                FindAllocation(formattedAllocations, physicalDiskNumber),
                FindAllocation(availableAllocations, physicalDiskNumber));
        }

        return new DiskVolumeRoleMapping(isSystemVolume, hasPageFile);
    }

    private static ulong FindAllocation(
        ReadOnlySpan<DiskByteAllocation> allocations,
        uint physicalDiskNumber)
    {
        for (int allocationIndex = 0; allocationIndex < allocations.Length; allocationIndex++)
        {
            if (allocations[allocationIndex].PhysicalDiskNumber == physicalDiskNumber)
                return allocations[allocationIndex].Bytes;
        }

        return 0;
    }

    private static DiskMetadataBuilder GetOrCreateBuilder(
        Dictionary<uint, DiskMetadataBuilder> metadataByDiskNumber,
        uint physicalDiskNumber)
    {
        if (metadataByDiskNumber.TryGetValue(
                physicalDiskNumber,
                out DiskMetadataBuilder? builder))
            return builder;

        builder = new DiskMetadataBuilder(physicalDiskNumber);
        metadataByDiskNumber.Add(physicalDiskNumber, builder);
        return builder;
    }

    private static List<string> EnumerateVolumeNames()
    {
        List<string> volumeNames = [];
        char[] volumeNameBuffer = new char[VolumeNameBufferLength];
        IntPtr searchHandle = FindFirstVolumeW(
            volumeNameBuffer,
            (uint)volumeNameBuffer.Length);
        if (searchHandle == new IntPtr(-1)) return volumeNames;

        try
        {
            while (true)
            {
                string volumeName = NormalizeVolumeName(
                    ReadNullTerminatedString(volumeNameBuffer));
                if (volumeName.Length > 0) volumeNames.Add(volumeName);

                Array.Clear(volumeNameBuffer);
                if (FindNextVolumeW(
                        searchHandle,
                        volumeNameBuffer,
                        (uint)volumeNameBuffer.Length))
                    continue;

                break;
            }
        }
        finally
        {
            _ = FindVolumeClose(searchHandle);
        }

        volumeNames.Sort(StringComparer.OrdinalIgnoreCase);
        return volumeNames;
    }

    private static Dictionary<string, string> CreateNativeDeviceVolumeMap(
        List<string> volumeNames)
    {
        Dictionary<string, string> volumeByNativeDevicePath =
            new(StringComparer.OrdinalIgnoreCase);
        for (int volumeIndex = 0; volumeIndex < volumeNames.Count; volumeIndex++)
        {
            string volumeName = volumeNames[volumeIndex];
            string queryName = volumeName.StartsWith(
                ExtendedPathPrefix,
                StringComparison.Ordinal)
                ? volumeName[ExtendedPathPrefix.Length..]
                : volumeName;
            string[] nativePaths = QueryDosDeviceTargets(queryName);
            for (int pathIndex = 0; pathIndex < nativePaths.Length; pathIndex++)
            {
                string nativePath = nativePaths[pathIndex].TrimEnd(Path.DirectorySeparatorChar);
                if (!nativePath.StartsWith(
                        NativeDevicePathPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                volumeByNativeDevicePath.TryAdd(nativePath, volumeName);
            }
        }

        return volumeByNativeDevicePath;
    }

    private static string[] QueryDosDeviceTargets(string queryName)
    {
        int bufferLength = InitialDosDeviceBufferLength;
        while (true)
        {
            char[] targets = new char[bufferLength];
            uint characterCount = QueryDosDeviceW(
                queryName,
                targets,
                (uint)targets.Length);
            if (characterCount > 0)
            {
                int validLength = Math.Min(targets.Length, checked((int)characterCount));
                return ParseMultiString(targets.AsSpan(start: 0, validLength));
            }

            if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer
                || bufferLength == MaximumDosDeviceBufferLength)
                return [];

            bufferLength = Math.Min(bufferLength * 2, MaximumDosDeviceBufferLength);
        }
    }

    private static string ReadSystemVolumeName(out bool hasSystemDiskData)
    {
        string windowsDirectory = ReadSystemWindowsDirectory();
        hasSystemDiskData = TryResolvePathVolumeName(
            windowsDirectory,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            out string volumeName);
        return hasSystemDiskData ? volumeName : string.Empty;
    }

    private static string ReadSystemWindowsDirectory()
    {
        int bufferLength = InitialPathBufferLength;
        while (bufferLength <= MaximumPathBufferLength)
        {
            char[] path = new char[bufferLength];
            uint characterCount = GetSystemWindowsDirectoryW(path, (uint)path.Length);
            if (characterCount == 0) return string.Empty;
            if (characterCount < path.Length)
                return new string(path, startIndex: 0, checked((int)characterCount));

            if (characterCount > MaximumPathBufferLength) return string.Empty;
            bufferLength = checked((int)characterCount + 1);
        }

        return string.Empty;
    }

    private static HashSet<string> ReadPageFileVolumeNames(
        Dictionary<string, string> volumeByNativeDevicePath,
        out bool hasPageFileData)
    {
        HashSet<string> volumeNames = new(StringComparer.OrdinalIgnoreCase);
        string windowsDirectory = ReadSystemWindowsDirectory();
        PageFilePathCollector collector = new();
        GCHandle collectorHandle = GCHandle.Alloc(collector);
        try
        {
            unsafe
            {
                delegate* unmanaged[Stdcall]<IntPtr, IntPtr, char*, int> callback =
                    &CollectPageFilePath;
                if (!EnumPageFilesW(callback, GCHandle.ToIntPtr(collectorHandle)))
                {
                    collector.FailureMessage = string.Create(
                        CultureInfo.InvariantCulture,
                        $"EnumPageFilesW failed ({Marshal.GetLastPInvokeError()}).");
                }
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException
                                              or MarshalDirectiveException)
        {
            collector.FailureMessage = exception.Message;
        }
        finally
        {
            collectorHandle.Free();
        }

        hasPageFileData = collector.FailureMessage.Length == 0;
        if (!hasPageFileData)
        {
            TADNLog.LogDebug(
                $"DiskDeviceMetadataReader page-file discovery: {collector.FailureMessage}");
        }

        for (int pathIndex = 0; pathIndex < collector.Paths.Count; pathIndex++)
        {
            string pageFilePath = NormalizePageFilePath(
                collector.Paths[pathIndex],
                windowsDirectory);
            if (TryResolvePathVolumeName(
                    pageFilePath,
                    volumeByNativeDevicePath,
                    out string volumeName))
                volumeNames.Add(volumeName);
            else
                hasPageFileData = false;
        }

        return volumeNames;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int CollectPageFilePath(
        IntPtr context,
        IntPtr pageFileInformation,
        char* fileName)
    {
        _ = pageFileInformation;
        PageFilePathCollector? collector = null;
        try
        {
            object? target = GCHandle.FromIntPtr(context).Target;
            collector = target as PageFilePathCollector;
            if (collector is null || fileName is null) return 0;

            collector.Paths.Add(new string(fileName));
            return 1;
        }
        catch (Exception exception)
        {
            collector?.FailureMessage = exception.Message;
            return 0;
        }
    }

    internal static string NormalizePageFilePath(
        string pageFilePath,
        string windowsDirectory)
    {
        string normalizedPath = pageFilePath.Trim();
        if (normalizedPath.StartsWith(NativeDosPathPrefix, StringComparison.Ordinal))
            normalizedPath = normalizedPath[NativeDosPathPrefix.Length..];
        if (normalizedPath.StartsWith(SystemRootPrefix, StringComparison.OrdinalIgnoreCase)
            && windowsDirectory.Length > 0)
        {
            normalizedPath = string.Concat(
                windowsDirectory,
                normalizedPath[SystemRootPrefix.Length..]);
        }

        return normalizedPath;
    }

    private static bool TryResolvePathVolumeName(
        string path,
        Dictionary<string, string> volumeByNativeDevicePath,
        out string volumeName)
    {
        volumeName = string.Empty;
        if (path.Length == 0) return false;

        if (path.StartsWith(NativeDevicePathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string bestNativePath = string.Empty;
            foreach (string nativePath in volumeByNativeDevicePath.Keys)
            {
                if (nativePath.Length <= bestNativePath.Length
                    || !PathStartsWithDeviceName(path, nativePath))
                    continue;

                bestNativePath = nativePath;
            }

            if (bestNativePath.Length == 0) return false;
            volumeName = volumeByNativeDevicePath[bestNativePath];
            return true;
        }

        char[] volumePath = new char[MaximumPathBufferLength];
        if (!GetVolumePathNameW(path, volumePath, (uint)volumePath.Length)) return false;

        string mountPoint = EnsureTrailingDirectorySeparator(
            ReadNullTerminatedString(volumePath));
        if (mountPoint.Length == 0) return false;
        if (mountPoint.StartsWith(ExtendedPathPrefix + "Volume{", StringComparison.OrdinalIgnoreCase))
        {
            volumeName = NormalizeVolumeName(mountPoint);
            return true;
        }

        char[] volumeNameBuffer = new char[VolumeNameBufferLength];
        if (!GetVolumeNameForVolumeMountPointW(
                mountPoint,
                volumeNameBuffer,
                (uint)volumeNameBuffer.Length))
            return false;

        volumeName = NormalizeVolumeName(ReadNullTerminatedString(volumeNameBuffer));
        return volumeName.Length > 0;
    }

    private static bool PathStartsWithDeviceName(string path, string deviceName)
    {
        if (!path.StartsWith(deviceName, StringComparison.OrdinalIgnoreCase)) return false;
        return path.Length == deviceName.Length
               || path[deviceName.Length] == Path.DirectorySeparatorChar;
    }

    private static string[] ReadVolumeDisplayNames(string volumeName)
    {
        int bufferLength = InitialPathBufferLength;
        while (true)
        {
            char[] paths = new char[bufferLength];
            bool succeeded = GetVolumePathNamesForVolumeNameW(
                volumeName,
                paths,
                (uint)paths.Length,
                out uint requiredLength);
            if (succeeded)
            {
                string[] allPaths = ParseMultiString(paths);
                SortedSet<string> driveLetters =
                    new(StringComparer.OrdinalIgnoreCase);
                for (int pathIndex = 0; pathIndex < allPaths.Length; pathIndex++)
                {
                    string path = allPaths[pathIndex];
                    if (path.Length < 3
                        || path[1] != ':'
                        || path[2] != Path.DirectorySeparatorChar
                        || !char.IsAsciiLetter(path[0]))
                        continue;

                    driveLetters.Add(string.Concat(
                        char.ToUpperInvariant(path[0]),
                        arg1: ":"));
                }

                string[] result = new string[driveLetters.Count];
                driveLetters.CopyTo(result);
                return result;
            }

            if (Marshal.GetLastPInvokeError() != ErrorMoreData
                || requiredLength <= paths.Length
                || requiredLength > MaximumPathBufferLength)
                return [];

            bufferLength = checked((int)requiredLength);
        }
    }

    private static DiskMediaKind ReadMediaKind(uint physicalDiskNumber)
    {
        string devicePath = string.Create(
            CultureInfo.InvariantCulture,
            $@"\\.\PhysicalDrive{physicalDiskNumber}");
        using SafeFileHandle diskHandle = OpenDevice(devicePath);
        if (diskHandle.IsInvalid) return DiskMediaKind.Unknown;

        byte[] query = new byte[StoragePropertyQuerySize];
        BinaryPrimitives.WriteInt32LittleEndian(query, StorageDeviceSeekPenaltyProperty);
        BinaryPrimitives.WriteInt32LittleEndian(query.AsSpan(sizeof(int)), PropertyStandardQuery);
        byte[] descriptor = new byte[16];
        if (!DeviceIoControlByteBuffers(
                diskHandle,
                IOCTLStorageQueryProperty,
                query,
                (uint)query.Length,
                descriptor,
                (uint)descriptor.Length,
                out uint bytesReturned,
                IntPtr.Zero))
            return DiskMediaKind.Unknown;

        int validLength = Math.Min(descriptor.Length, checked((int)bytesReturned));
        return ParseSeekPenaltyDescriptor(descriptor.AsSpan(start: 0, validLength));
    }

    private static bool TryReadVolumeDiskExtents(
        SafeFileHandle volumeHandle,
        out DiskVolumeExtent[] extents)
    {
        extents = [];
        int bufferLength = InitialExtentBufferLength;
        while (true)
        {
            byte[] descriptor = new byte[bufferLength];
            bool succeeded = DeviceIoControlOutputBuffer(
                volumeHandle,
                IOCTLVolumeGetVolumeDiskExtents,
                IntPtr.Zero,
                inputBufferSize: 0,
                descriptor,
                (uint)descriptor.Length,
                out uint bytesReturned,
                IntPtr.Zero);
            if (succeeded)
            {
                int validLength = Math.Min(descriptor.Length, checked((int)bytesReturned));
                return TryParseVolumeDiskExtents(
                    descriptor.AsSpan(start: 0, validLength),
                    out extents);
            }

            if (Marshal.GetLastPInvokeError() != ErrorMoreData
                || descriptor.Length < sizeof(uint))
                return false;

            uint extentCount = BinaryPrimitives.ReadUInt32LittleEndian(descriptor);
            long requiredLength = VolumeExtentArrayOffset + (long)extentCount * DiskExtentSize;
            if (requiredLength <= bufferLength
                || requiredLength > MaximumExtentBufferLength)
                return false;

            bufferLength = checked((int)requiredLength);
        }
    }

    private static SafeFileHandle OpenDevice(string path) =>
        CreateFileW(
            path,
            desiredAccess: 0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            flagsAndAttributes: 0,
            IntPtr.Zero);

    private static string NormalizeVolumeName(string volumeName) =>
        volumeName.Trim().TrimEnd(Path.DirectorySeparatorChar);

    private static string EnsureTrailingDirectorySeparator(string path) =>
        path.Length > 0 && path[^1] != Path.DirectorySeparatorChar
            ? string.Concat(path, Path.DirectorySeparatorChar)
            : path;

    private static string ReadNullTerminatedString(char[] characters)
    {
        int terminatorIndex = Array.IndexOf(characters, value: '\0');
        int length = terminatorIndex >= 0 ? terminatorIndex : characters.Length;
        return new string(characters, startIndex: 0, length);
    }

    private static string[] ParseMultiString(ReadOnlySpan<char> value)
    {
        List<string> items = [];
        int position = 0;
        while (position < value.Length)
        {
            ReadOnlySpan<char> remaining = value[position..];
            int terminatorOffset = remaining.IndexOf('\0');
            int itemLength = terminatorOffset >= 0 ? terminatorOffset : remaining.Length;
            if (itemLength == 0) break;

            items.Add(remaining[..itemLength].ToString());
            position += itemLength + 1;
        }

        return [.. items];
    }

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
    private static extern IntPtr FindFirstVolumeW(
        [Out] char[] volumeName,
        uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextVolumeW(
        IntPtr searchHandle,
        [Out] char[] volumeName,
        uint bufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindVolumeClose(IntPtr searchHandle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(
        string deviceName,
        [Out] char[] targetPath,
        uint maximumCharacterCount);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNamesForVolumeNameW(
        string volumeName,
        [Out] char[] volumePathNames,
        uint bufferLength,
        out uint returnLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(
        string fileName,
        [Out] char[] volumePathName,
        uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(
        string volumeMountPoint,
        [Out] char[] volumeName,
        uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetSystemWindowsDirectoryW(
        [Out] char[] buffer,
        uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string directoryName,
        out ulong freeBytesAvailableToCaller,
        out ulong totalNumberOfBytes,
        out ulong totalNumberOfFreeBytes);

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

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool EnumPageFilesW(
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, char*, int> callback,
        IntPtr context);

    private sealed class DiskMetadataBuilder(uint physicalDiskNumber)
    {
        private readonly SortedSet<string> _volumeNames =
            new(StringComparer.OrdinalIgnoreCase);

        public bool HasVolumeData;
        public ulong FormattedCapacityBytes;
        public ulong AvailableBytes;
        public bool IsSystemDisk;
        public bool HasPageFile;

        public void AddVolumeData(
            string[] volumeNames,
            ulong formattedCapacityBytes,
            ulong availableBytes)
        {
            HasVolumeData = true;
            for (int nameIndex = 0; nameIndex < volumeNames.Length; nameIndex++)
                _volumeNames.Add(volumeNames[nameIndex]);
            FormattedCapacityBytes = SaturatingAdd(
                FormattedCapacityBytes,
                formattedCapacityBytes);
            AvailableBytes = SaturatingAdd(AvailableBytes, availableBytes);
        }

        public void MarkVolumeRoles(bool isSystemDisk, bool hasPageFile)
        {
            IsSystemDisk |= isSystemDisk;
            HasPageFile |= hasPageFile;
        }

        public DiskDeviceMetadataSnapshot Build(
            DiskMediaKind mediaKind,
            bool hasSystemDiskData,
            bool hasPageFileData) => new(
            HasDeviceData: true,
            physicalDiskNumber,
            HasVolumeData,
            string.Join(separator: ", ", _volumeNames),
            FormattedCapacityBytes,
            AvailableBytes,
            hasSystemDiskData,
            IsSystemDisk,
            hasPageFileData,
            HasPageFile,
            mediaKind);

        private static ulong SaturatingAdd(ulong left, ulong right) =>
            left > ulong.MaxValue - right ? ulong.MaxValue : left + right;
    }

    private sealed class AllocationBuilder(
        uint physicalDiskNumber,
        ulong bytes,
        UInt128 remainder)
    {
        public readonly uint PhysicalDiskNumber = physicalDiskNumber;
        public ulong Bytes = bytes;
        public readonly UInt128 Remainder = remainder;
    }

    private sealed class PageFilePathCollector
    {
        public readonly List<string> Paths = [];
        public string FailureMessage = string.Empty;
    }

    private readonly record struct DiskVolumeRoleMapping(
        bool IsSystemVolume,
        bool HasPageFile);
}
