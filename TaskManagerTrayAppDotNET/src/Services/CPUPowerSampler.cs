using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// Reads CPU package power from Windows' Energy Meter Interface (EMI), as Chromium's
/// <c>energy_metrics_provider_win.cc</c> does. The processor power driver exposes one EMI device per core; the
/// package channel (<c>RAPL_Package0_PKG</c>, or an AMD APU's socket channel) sits on one of them. EMI needs no
/// elevation and no third-party driver. Power is the energy difference between two samples over the driver's own
/// timestamps.
/// </summary>
internal sealed class CPUPowerSampler : IDisposable
{
    // emi.h: CTL_CODE(FILE_DEVICE_UNKNOWN, n, METHOD_BUFFERED, FILE_READ_ACCESS)
    private const uint IOCTLEMIGetVersion = 0x00224000;
    private const uint IOCTLEMIGetMetadataSize = 0x00224004;
    private const uint IOCTLEMIGetMetadata = 0x00224008;
    private const uint IOCTLEMIGetMeasurement = 0x0022400C;
    private const ushort EMIVersion1 = 1;
    private const ushort EMIVersion2 = 2;
    private const int EMINameBytes = 16 * sizeof(char);
    private const int ChannelMeasurementBytes = 2 * sizeof(ulong);
    private const int MaximumMetadataBytes = 64 * 1024;

    // EMI_METADATA_V1: MeasurementUnit, HardwareOEM, HardwareModel, HardwareRevision, MeteredHardwareNameSize
    private const int MetadataV1NameSizeOffset = sizeof(int) + 2 * EMINameBytes + sizeof(ushort);
    private const int MetadataV1NameOffset = MetadataV1NameSizeOffset + sizeof(ushort);

    // EMI_METADATA_V2: HardwareOEM, HardwareModel, HardwareRevision, ChannelCount, then packed EMI_CHANNEL_V2s
    private const int MetadataV2ChannelCountOffset = 2 * EMINameBytes + sizeof(ushort);
    private const int MetadataV2ChannelsOffset = MetadataV2ChannelCountOffset + sizeof(ushort);
    private const int ChannelV2NameOffset = sizeof(int) + sizeof(ushort);

    private const uint GenericRead = 0x80000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const int ConfigManagerSuccess = 0;
    private const int ConfigManagerBufferSmall = 0x1A;
    private const uint InterfaceListPresent = 0;

    // 1 pWh = 3.6e-9 J and the EMI timestamps count 100 ns intervals
    private const double JoulesPerPicowattHour = 3.6e-9;
    private const double SecondsPerTimeUnit = 1e-7;

    private static readonly Guid EnergyMeterInterface = new("45bd8344-7ed6-49cf-a440-c276c933b053");

    private static readonly string[] SocketChannelNames = ["Current Socket Energy", "Apu Energy"];

    private PackageMeter[] _meters = [];
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Returns the package power since the previous call. The first call, a counter reset, and a gap longer than
    /// <paramref name="maximumSampleSpan"/> (sleep, a stalled sampler) only re-establish the baseline.
    /// </summary>
    public bool TrySample(TimeSpan maximumSampleSpan, out double watts)
    {
        watts = 0;
        if (_disposed) return false;

        if (!_initialized)
        {
            _initialized = true;
            _meters = OpenPackageMeters();
        }

        if (_meters.Length == 0) return false;

        bool complete = true;
        double total = 0;
        for (int meterIndex = 0; meterIndex < _meters.Length; meterIndex++)
        {
            PackageMeter meter = _meters[meterIndex];
            if (!meter.TryRead(out ulong energy, out ulong time))
            {
                meter.HasBaseline = false;
                complete = false;
                continue;
            }

            double meterWatts = 0;
            bool hasPower = meter.HasBaseline
                            && TryCalculateWatts(
                                meter.Energy,
                                meter.Time,
                                energy,
                                time,
                                maximumSampleSpan,
                                out meterWatts);
            meter.Energy = energy;
            meter.Time = time;
            meter.HasBaseline = true;
            if (hasPower) total += meterWatts;
            else complete = false;
        }

        if (!complete) return false;

        watts = total;
        return true;
    }

    /// <summary>Converts two cumulative picowatt-hour readings and their 100 ns timestamps to watts.</summary>
    internal static bool TryCalculateWatts(
        ulong previousEnergy,
        ulong previousTime,
        ulong energy,
        ulong time,
        TimeSpan maximumSampleSpan,
        out double watts)
    {
        watts = 0;
        if (time <= previousTime || energy < previousEnergy) return false;

        ulong elapsed = time - previousTime;
        if (elapsed > (ulong)maximumSampleSpan.Ticks) return false;

        watts = (energy - previousEnergy) * JoulesPerPicowattHour / (elapsed * SecondsPerTimeUnit);
        return double.IsFinite(watts);
    }

    /// <summary>Reads the channel names from an <c>EMI_METADATA_V1</c> or <c>EMI_METADATA_V2</c> buffer.</summary>
    internal static string[] ParseChannelNames(ReadOnlySpan<byte> metadata, ushort version)
    {
        if (version == EMIVersion1)
        {
            if (metadata.Length < MetadataV1NameOffset) return [];

            int nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(metadata[MetadataV1NameSizeOffset..]);
            return metadata.Length - MetadataV1NameOffset < nameBytes
                ? []
                : [DecodeName(metadata.Slice(MetadataV1NameOffset, nameBytes))];
        }

        if (version != EMIVersion2 || metadata.Length < MetadataV2ChannelsOffset) return [];

        int channelCount = BinaryPrimitives.ReadUInt16LittleEndian(metadata[MetadataV2ChannelCountOffset..]);
        string[] names = new string[channelCount];
        int offset = MetadataV2ChannelsOffset;
        for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
        {
            if (metadata.Length - offset < ChannelV2NameOffset) return [];

            int nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(metadata[(offset + sizeof(int))..]);
            int nameOffset = offset + ChannelV2NameOffset;
            if (metadata.Length - nameOffset < nameBytes) return [];

            names[channelIndex] = DecodeName(metadata.Slice(nameOffset, nameBytes));
            offset = nameOffset + nameBytes;
        }

        return names;
    }

    /// <summary>
    /// Picks the channel that covers the whole package: Intel's and AMD's <c>RAPL_PackageN_PKG</c>, or the socket
    /// channel AMD APUs report instead. Per-core, DRAM and voltage-rail channels are parts of it and never summed.
    /// </summary>
    internal static int SelectPackageChannel(ReadOnlySpan<string> channelNames)
    {
        for (int channelIndex = 0; channelIndex < channelNames.Length; channelIndex++)
        {
            if (IsRAPLPackageChannel(channelNames[channelIndex])) return channelIndex;
        }

        foreach (string socketChannelName in SocketChannelNames)
        {
            for (int channelIndex = 0; channelIndex < channelNames.Length; channelIndex++)
            {
                if (string.Equals(channelNames[channelIndex], socketChannelName, StringComparison.OrdinalIgnoreCase))
                    return channelIndex;
            }
        }

        return -1;
    }

    private static bool IsRAPLPackageChannel(string name)
    {
        const string prefix = "RAPL_Package";
        const string suffix = "_PKG";
        if (name.Length <= prefix.Length + suffix.Length
            || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        ReadOnlySpan<char> packageNumber = name.AsSpan(prefix.Length, name.Length - prefix.Length - suffix.Length);
        foreach (char character in packageNumber)
        {
            if (!char.IsAsciiDigit(character)) return false;
        }

        return true;
    }

    private static string DecodeName(ReadOnlySpan<byte> name) =>
        Encoding.Unicode.GetString(name[..(name.Length & ~1)]).TrimEnd('\0').Trim();

    private static PackageMeter[] OpenPackageMeters()
    {
        List<PackageMeter> meters = [];
        HashSet<string> channelNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string devicePath in ReadInterfacePaths())
        {
            SafeFileHandle device = CreateFileW(
                devicePath,
                GenericRead,
                FileShareReadWrite,
                IntPtr.Zero,
                OpenExisting,
                flagsAndAttributes: 0,
                IntPtr.Zero);
            if (device.IsInvalid)
            {
                device.Dispose();
                continue;
            }

            PackageMeter? meter = TryCreateMeter(device, channelNames);
            if (meter == null) device.Dispose();
            else meters.Add(meter);
        }

        return [.. meters];
    }

    private static PackageMeter? TryCreateMeter(SafeFileHandle device, HashSet<string> channelNames)
    {
        byte[] versionBuffer = new byte[sizeof(ushort)];
        if (!Query(device, IOCTLEMIGetVersion, versionBuffer, out int versionBytes) || versionBytes < sizeof(ushort))
            return null;

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(versionBuffer);
        if (version is not (EMIVersion1 or EMIVersion2)) return null;

        byte[] sizeBuffer = new byte[sizeof(uint)];
        if (!Query(device, IOCTLEMIGetMetadataSize, sizeBuffer, out int sizeBytes) || sizeBytes < sizeof(uint))
            return null;

        uint metadataSize = BinaryPrimitives.ReadUInt32LittleEndian(sizeBuffer);
        if (metadataSize is 0 or > MaximumMetadataBytes) return null;

        byte[] metadata = new byte[metadataSize];
        if (!Query(device, IOCTLEMIGetMetadata, metadata, out int metadataBytes)) return null;

        string[] names = ParseChannelNames(metadata.AsSpan(0, metadataBytes), version);
        int channelIndex = SelectPackageChannel(names);

        // Every core's device can repeat a socket-wide channel; count each package once
        if (channelIndex < 0 || !channelNames.Add(names[channelIndex])) return null;

        int channelCount = version == EMIVersion1 ? 1 : names.Length;
        return new PackageMeter(device, channelIndex, channelCount);
    }

    private static bool Query(SafeFileHandle device, uint controlCode, byte[] output, out int bytesReturned)
    {
        bool result = DeviceIoControl(
            device,
            controlCode,
            IntPtr.Zero,
            inputBufferSize: 0,
            output,
            (uint)output.Length,
            out uint returned,
            IntPtr.Zero);
        bytesReturned = (int)Math.Min(returned, (uint)output.Length);
        return result;
    }

    private static List<string> ReadInterfacePaths()
    {
        Guid interfaceClass = EnergyMeterInterface;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_Interface_List_SizeW(out uint length, ref interfaceClass, deviceID: null, InterfaceListPresent)
                    != ConfigManagerSuccess
                || length <= 1)
                return [];

            char[] buffer = new char[length];
            int result = CM_Get_Device_Interface_ListW(ref interfaceClass, deviceID: null, buffer, length, InterfaceListPresent);
            if (result == ConfigManagerBufferSmall) continue;
            if (result != ConfigManagerSuccess) return [];

            List<string> paths = [];
            int start = 0;
            for (int index = 0; index < buffer.Length; index++)
            {
                if (buffer[index] != '\0') continue;
                if (index == start) break;

                paths.Add(new string(buffer, start, index - start));
                start = index + 1;
            }

            return paths;
        }

        return [];
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        foreach (PackageMeter meter in _meters)
            meter.Dispose();
        _meters = [];
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        uint inputBufferSize,
        [Out] byte[] outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(
        out uint length,
        ref Guid interfaceClass,
        string? deviceID,
        uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(
        ref Guid interfaceClass,
        string? deviceID,
        [Out] char[] buffer,
        uint bufferLength,
        uint flags);

    /// <summary>One open EMI device and the cumulative reading of its package channel.</summary>
    private sealed class PackageMeter(SafeFileHandle device, int channelIndex, int channelCount) : IDisposable
    {
        private readonly byte[] _measurement = new byte[channelCount * ChannelMeasurementBytes];

        public ulong Energy { get; set; }
        public ulong Time { get; set; }
        public bool HasBaseline { get; set; }

        public bool TryRead(out ulong energy, out ulong time)
        {
            energy = 0;
            time = 0;
            if (!Query(device, IOCTLEMIGetMeasurement, _measurement, out int bytesReturned)
                || bytesReturned < (channelIndex + 1) * ChannelMeasurementBytes)
                return false;

            ReadOnlySpan<byte> channel = _measurement.AsSpan(channelIndex * ChannelMeasurementBytes);
            energy = BinaryPrimitives.ReadUInt64LittleEndian(channel);
            time = BinaryPrimitives.ReadUInt64LittleEndian(channel[sizeof(ulong)..]);
            return true;
        }

        public void Dispose() => device.Dispose();
    }
}
