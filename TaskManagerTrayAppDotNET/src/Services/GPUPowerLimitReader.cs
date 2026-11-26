using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>The GPU a power-limit read targets: its vendor, PnP instance and the PCI address the kernel reports.</summary>
internal readonly record struct GPUPowerLimitTarget(ushort VendorID, string DeviceInstanceID, GPUPCIAddress Address);

/// <summary>The outcome of one power-limit read: the board's default limit, or why it could not be read.</summary>
internal readonly record struct GPUPowerLimitResult(bool Succeeded, uint DefaultLimitMilliwatts, string Error);

/// <summary>
/// Reads a GPU's default power limit through the monitoring library its driver installs (NVML, ADLX or IGCL). The
/// read runs in a short-lived child process, so the vendor DLL is never loaded into the app: a crash in vendor code
/// cannot take the app down, and a driver update never finds the DLL locked.
/// </summary>
internal static class GPUPowerLimitReader
{
    public const string HelperArgument = "--read-gpu-power-limit";

    private const string NVIDIALibrary = "nvml.dll";
    private const string AMDLibrary = "amdadlx64.dll";
    private const string IntelLibrary = "ControlLib.dll";
    private const string SuccessPrefix = "OK ";
    private const string ErrorPrefix = "ERROR ";
    private const int HelperArgumentCount = 6;
    private const int HelperTimeoutMilliseconds = 20_000;

    /// <summary>Returns the vendor library file name, or an empty string for a vendor without one.</summary>
    public static string GetLibraryFileName(ushort vendorID) =>
        vendorID switch
        {
            GPUVendorFormatting.NVIDIAVendorID => NVIDIALibrary,
            GPUVendorFormatting.AMDVendorID => AMDLibrary,
            GPUVendorFormatting.IntelVendorID => IntelLibrary,
            _ => string.Empty
        };

    /// <summary>Finds the vendor library where drivers install it, without loading it.</summary>
    public static bool TryFindLibrary(ushort vendorID, out string path)
    {
        path = string.Empty;
        string fileName = GetLibraryFileName(vendorID);
        if (fileName.Length == 0) return false;

        string systemPath = Path.Combine(Environment.SystemDirectory, fileName);
        if (File.Exists(systemPath))
        {
            path = systemPath;
            return true;
        }

        // Drivers before R460 kept NVML only beside nvidia-smi
        if (vendorID != GPUVendorFormatting.NVIDIAVendorID) return false;

        string legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA Corporation",
            "NVSMI",
            NVIDIALibrary);
        if (!File.Exists(legacyPath)) return false;

        path = legacyPath;
        return true;
    }

    /// <summary>Starts this executable in helper mode and waits for the limit it prints.</summary>
    public static async Task<GPUPowerLimitResult> ReadAsync(GPUPowerLimitTarget target, CancellationToken cancellationToken)
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
            return Failure("The app's own executable path is unknown.");

        ProcessStartInfo startInfo = new(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add(HelperArgument);
        startInfo.ArgumentList.Add(target.VendorID.ToString("X4", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(FormatAddressPart(target.Address.HasValue, target.Address.Bus));
        startInfo.ArgumentList.Add(FormatAddressPart(target.Address.HasValue, target.Address.Device));
        startInfo.ArgumentList.Add(FormatAddressPart(target.Address.HasValue, target.Address.Function));
        startInfo.ArgumentList.Add(target.DeviceInstanceID);

        using Process? process = Process.Start(startInfo);
        if (process == null) return Failure("The reader process did not start.");

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HelperTimeoutMilliseconds);
        try
        {
            string output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return ParseHelperOutput(output);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return cancellationToken.IsCancellationRequested
                ? Failure("The read was cancelled.")
                : Failure("The vendor library did not answer in time.");
        }
    }

    /// <summary>Parses the single line the helper prints: <c>OK &lt;milliwatts&gt;</c> or <c>ERROR &lt;reason&gt;</c>.</summary>
    internal static GPUPowerLimitResult ParseHelperOutput(string output)
    {
        string line = output.Trim();
        if (line.StartsWith(SuccessPrefix, StringComparison.Ordinal)
            && uint.TryParse(
                line.AsSpan(SuccessPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out uint milliwatts)
            && milliwatts > 0)
            return new GPUPowerLimitResult(Succeeded: true, milliwatts, string.Empty);

        if (line.StartsWith(ErrorPrefix, StringComparison.Ordinal))
            return Failure(line[ErrorPrefix.Length..].Trim());

        return Failure(line.Length == 0 ? "The reader process printed nothing." : line);
    }

    /// <summary>Recognizes the helper launch; checked before every other mode so it never starts the app.</summary>
    public static bool IsHelperLaunch(string[] args) =>
        args.Length > 0 && string.Equals(args[0], HelperArgument, StringComparison.Ordinal);

    /// <summary>Runs the helper: loads the vendor library, prints one result line, and exits.</summary>
    public static int RunHelper(string[] args)
    {
        GPUPowerLimitResult result;
        if (!TryParseHelperArguments(args, out GPUPowerLimitTarget target))
            result = Failure("The reader was started with invalid arguments.");
        else if (!TryFindLibrary(target.VendorID, out string libraryPath))
            result = Failure(string.Concat(GetLibraryFileName(target.VendorID), " is not installed."));
        else
        {
            try
            {
                result = target.VendorID switch
                {
                    GPUVendorFormatting.NVIDIAVendorID => NVMLPowerLimit.Read(libraryPath, target),
                    GPUVendorFormatting.AMDVendorID => ADLXPowerLimit.Read(libraryPath, target),
                    GPUVendorFormatting.IntelVendorID => IGCLPowerLimit.Read(libraryPath, target),
                    _ => Failure("This GPU vendor has no supported monitoring library.")
                };
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                                  or EntryPointNotFoundException
                                                  or BadImageFormatException
                                                  or SEHException)
            {
                result = Failure(exception.Message);
            }
        }

        string line = result.Succeeded
            ? string.Concat(SuccessPrefix, result.DefaultLimitMilliwatts.ToString(CultureInfo.InvariantCulture))
            : string.Concat(ErrorPrefix, result.Error.ReplaceLineEndings(" "));
        using Stream standardOutput = Console.OpenStandardOutput();
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        standardOutput.Write(bytes);
        return result.Succeeded ? 0 : 1;
    }

    internal static bool TryParseHelperArguments(string[] args, out GPUPowerLimitTarget target)
    {
        target = default;
        if (args.Length != HelperArgumentCount
            || !IsHelperLaunch(args)
            || !ushort.TryParse(args[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort vendorID))
            return false;

        bool hasBus = TryParseAddressPart(args[2], out uint bus);
        bool hasDevice = TryParseAddressPart(args[3], out uint device);
        bool hasFunction = TryParseAddressPart(args[4], out uint function);
        GPUPCIAddress address = hasBus && hasDevice && hasFunction
            ? new GPUPCIAddress(HasValue: true, bus, device, function)
            : default;
        target = new GPUPowerLimitTarget(vendorID, args[5], address);
        return true;
    }

    internal static GPUPowerLimitResult Failure(string error) =>
        new(Succeeded: false, DefaultLimitMilliwatts: 0, error);

    private static string FormatAddressPart(bool hasAddress, uint value) =>
        hasAddress ? value.ToString(CultureInfo.InvariantCulture) : "-";

    private static bool TryParseAddressPart(string value, out uint parsed) =>
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}

/// <summary>A loaded vendor library that frees itself; exports are resolved by name.</summary>
internal sealed class VendorLibrary : IDisposable
{
    private readonly IntPtr _handle;

    private VendorLibrary(IntPtr handle) => _handle = handle;

    public static VendorLibrary? TryLoad(string path) =>
        NativeLibrary.TryLoad(path, out IntPtr handle) ? new VendorLibrary(handle) : null;

    public IntPtr GetExport(string name) =>
        NativeLibrary.TryGetExport(_handle, name, out IntPtr export)
            ? export
            : throw new EntryPointNotFoundException(string.Concat("The vendor library has no export ", name, "."));

    public void Dispose() => NativeLibrary.Free(_handle);
}

/// <summary>NVIDIA: <c>nvmlDeviceGetPowerManagementDefaultLimit</c> on the device at the adapter's PCI address.</summary>
internal static unsafe class NVMLPowerLimit
{
    private const int Success = 0;

    public static GPUPowerLimitResult Read(string libraryPath, GPUPowerLimitTarget target)
    {
        using VendorLibrary? library = VendorLibrary.TryLoad(libraryPath);
        if (library == null) return GPUPowerLimitReader.Failure("nvml.dll could not be loaded.");

        var init = (delegate* unmanaged<int>)library.GetExport("nvmlInit_v2");
        var shutdown = (delegate* unmanaged<int>)library.GetExport("nvmlShutdown");
        var errorString = (delegate* unmanaged<int, byte*>)library.GetExport("nvmlErrorString");
        var getCount = (delegate* unmanaged<uint*, int>)library.GetExport("nvmlDeviceGetCount_v2");
        var getHandle = (delegate* unmanaged<uint, IntPtr*, int>)library.GetExport("nvmlDeviceGetHandleByIndex_v2");
        var getPCIInfo = (delegate* unmanaged<IntPtr, NVMLPCIInfo*, int>)library.GetExport("nvmlDeviceGetPciInfo_v3");
        var getDefaultLimit =
            (delegate* unmanaged<IntPtr, uint*, int>)library.GetExport("nvmlDeviceGetPowerManagementDefaultLimit");

        int result = init();
        if (result != Success) return Error("nvmlInit", result, errorString);

        try
        {
            uint count;
            result = getCount(&count);
            if (result != Success) return Error("nvmlDeviceGetCount", result, errorString);

            IntPtr match = IntPtr.Zero;
            int matches = 0;
            for (uint index = 0; index < count; index++)
            {
                IntPtr device;
                if (getHandle(index, &device) != Success) continue;

                NVMLPCIInfo info;
                if (getPCIInfo(device, &info) != Success) continue;

                // NVML's device number is the PCI device; it has no function field
                bool sameAddress = target.Address.HasValue
                                   && info.Bus == target.Address.Bus
                                   && info.Device == target.Address.Device;
                if (sameAddress)
                {
                    match = device;
                    matches = 1;
                    break;
                }

                if (!target.Address.HasValue)
                {
                    match = device;
                    matches++;
                }
            }

            if (matches != 1) return GPUPowerLimitReader.Failure("NVML did not report this GPU.");

            uint milliwatts;
            result = getDefaultLimit(match, &milliwatts);
            return result == Success && milliwatts > 0
                ? new GPUPowerLimitResult(Succeeded: true, milliwatts, string.Empty)
                : Error("nvmlDeviceGetPowerManagementDefaultLimit", result, errorString);
        }
        finally
        {
            _ = shutdown();
        }
    }

    private static GPUPowerLimitResult Error(string function, int result, delegate* unmanaged<int, byte*> errorString)
    {
        string? message = Marshal.PtrToStringUTF8((IntPtr)errorString(result));
        return GPUPowerLimitReader.Failure(string.Concat(function, ": ", message ?? result.ToString(CultureInfo.InvariantCulture)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NVMLPCIInfo
    {
        public fixed byte BusIDLegacy[16];
        public uint Domain;
        public uint Bus;
        public uint Device;
        public uint PCIDeviceID;
        public uint PCISubsystemID;
        public fixed byte BusID[32];
    }
}

/// <summary>
/// AMD: the upper end of ADLX's total-board-power range for the GPU whose PnP string matches. ADLX documents it only
/// as the maximum board power the GPU consumes; whether that equals the board's default power limit has not been
/// confirmed on AMD hardware.
/// </summary>
internal static unsafe class ADLXPowerLimit
{
    private const int Success = 0;
    private const ulong RequestedVersion = 1UL << 48;

    // Zero-based slots in the ADLX C vtables (SDK/Include/*.h); IADLXSystem has no Acquire/Release/QueryInterface prefix
    private const int SystemGetGPUs = 1;
    private const int SystemGetPerformanceMonitoringServices = 9;
    private const int InterfaceRelease = 1;
    private const int ListSize = 3;
    private const int ListAtGPU = 11;
    private const int GPUPNPString = 9;
    private const int MonitoringGetSupportedGPUMetrics = 21;
    private const int MetricsSupportIsSupportedTotalBoardPower = 9;
    private const int MetricsSupportGetTotalBoardPowerRange = 22;
    private const uint MilliwattsPerWatt = 1_000;

    public static GPUPowerLimitResult Read(string libraryPath, GPUPowerLimitTarget target)
    {
        using VendorLibrary? library = VendorLibrary.TryLoad(libraryPath);
        if (library == null) return GPUPowerLimitReader.Failure("amdadlx64.dll could not be loaded.");

        var initialize = (delegate* unmanaged<ulong, IntPtr*, int>)library.GetExport("ADLXInitialize");
        var terminate = (delegate* unmanaged<int>)library.GetExport("ADLXTerminate");
        IntPtr system;
        int result = initialize(RequestedVersion, &system);
        if (result != Success || system == IntPtr.Zero) return Error("ADLXInitialize", result);

        IntPtr gpus = IntPtr.Zero;
        IntPtr gpu = IntPtr.Zero;
        IntPtr services = IntPtr.Zero;
        IntPtr support = IntPtr.Zero;
        try
        {
            result = ((delegate* unmanaged<IntPtr, IntPtr*, int>)Slot(system, SystemGetGPUs))(system, &gpus);
            if (result != Success || gpus == IntPtr.Zero) return Error("IADLXSystem::GetGPUs", result);

            uint count = ((delegate* unmanaged<IntPtr, uint>)Slot(gpus, ListSize))(gpus);
            for (uint index = 0; index < count && gpu == IntPtr.Zero; index++)
            {
                IntPtr candidate;
                if (((delegate* unmanaged<IntPtr, uint, IntPtr*, int>)Slot(gpus, ListAtGPU))(gpus, index, &candidate) != Success
                    || candidate == IntPtr.Zero)
                    continue;

                byte* pnp;
                bool matches = ((delegate* unmanaged<IntPtr, byte**, int>)Slot(candidate, GPUPNPString))(candidate, &pnp) == Success
                               && IsSameDevice(Marshal.PtrToStringUTF8((IntPtr)pnp), target.DeviceInstanceID);
                if (matches || count == 1)
                    gpu = candidate;
                else
                    Release(candidate);
            }

            if (gpu == IntPtr.Zero) return GPUPowerLimitReader.Failure("ADLX did not report this GPU.");

            result = ((delegate* unmanaged<IntPtr, IntPtr*, int>)Slot(system, SystemGetPerformanceMonitoringServices))(
                system,
                &services);
            if (result != Success || services == IntPtr.Zero)
                return Error("IADLXSystem::GetPerformanceMonitoringServices", result);

            result = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Slot(services, MonitoringGetSupportedGPUMetrics))(
                services,
                gpu,
                &support);
            if (result != Success || support == IntPtr.Zero)
                return Error("IADLXPerformanceMonitoringServices::GetSupportedGPUMetrics", result);

            byte supported;
            result = ((delegate* unmanaged<IntPtr, byte*, int>)Slot(support, MetricsSupportIsSupportedTotalBoardPower))(
                support,
                &supported);
            if (result != Success || supported == 0)
                return GPUPowerLimitReader.Failure("ADLX does not report total board power for this GPU.");

            int minimumWatts;
            int maximumWatts;
            result = ((delegate* unmanaged<IntPtr, int*, int*, int>)Slot(support, MetricsSupportGetTotalBoardPowerRange))(
                support,
                &minimumWatts,
                &maximumWatts);
            return result == Success && maximumWatts > 0
                ? new GPUPowerLimitResult(Succeeded: true, (uint)maximumWatts * MilliwattsPerWatt, string.Empty)
                : Error("IADLXGPUMetricsSupport::GetGPUTotalBoardPowerRange", result);
        }
        finally
        {
            Release(support);
            Release(services);
            Release(gpu);
            Release(gpus);
            _ = terminate();
        }
    }

    private static bool IsSameDevice(string? pnpString, string deviceInstanceID) =>
        !string.IsNullOrEmpty(pnpString)
        && !string.IsNullOrEmpty(deviceInstanceID)
        && (string.Equals(pnpString, deviceInstanceID, StringComparison.OrdinalIgnoreCase)
            || deviceInstanceID.StartsWith(pnpString, StringComparison.OrdinalIgnoreCase));

    private static IntPtr Slot(IntPtr instance, int slot) => ((IntPtr*)*(IntPtr*)instance)[slot];

    private static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
            _ = ((delegate* unmanaged<IntPtr, long>)Slot(instance, InterfaceRelease))(instance);
    }

    private static GPUPowerLimitResult Error(string function, int result) =>
        GPUPowerLimitReader.Failure(string.Concat(
            function,
            " failed with ADLX result ",
            result.ToString(CultureInfo.InvariantCulture),
            "."));
}

/// <summary>Intel: the factory default limit of the GPU's first IGCL power domain whose PCI address matches.</summary>
internal static unsafe class IGCLPowerLimit
{
    private const int Success = 0;
    private const uint InitFlagUseLevelZero = 1;
    private const uint ImplementationVersion = (1u << 16) | 1u;
    private const byte AdapterPropertiesVersion = 2;
    private const int MaximumDevices = 16;
    private const int MaximumPowerDomains = 8;

    public static GPUPowerLimitResult Read(string libraryPath, GPUPowerLimitTarget target)
    {
        using VendorLibrary? library = VendorLibrary.TryLoad(libraryPath);
        if (library == null) return GPUPowerLimitReader.Failure("ControlLib.dll could not be loaded.");

        var init = (delegate* unmanaged<CTLInitArgs*, IntPtr*, int>)library.GetExport("ctlInit");
        var close = (delegate* unmanaged<IntPtr, int>)library.GetExport("ctlClose");
        var enumerateDevices = (delegate* unmanaged<IntPtr, uint*, IntPtr*, int>)library.GetExport("ctlEnumerateDevices");
        var getProperties =
            (delegate* unmanaged<IntPtr, CTLDeviceAdapterProperties*, int>)library.GetExport("ctlGetDeviceProperties");
        var enumeratePowerDomains =
            (delegate* unmanaged<IntPtr, uint*, IntPtr*, int>)library.GetExport("ctlEnumPowerDomains");
        var getPowerProperties =
            (delegate* unmanaged<IntPtr, CTLPowerProperties*, int>)library.GetExport("ctlPowerGetProperties");

        CTLInitArgs initArgs = new()
        {
            Size = (uint)sizeof(CTLInitArgs),
            AppVersion = ImplementationVersion,
            Flags = InitFlagUseLevelZero
        };
        IntPtr api;
        int result = init(&initArgs, &api);
        if (result != Success || api == IntPtr.Zero) return Error("ctlInit", result);

        try
        {
            uint count = MaximumDevices;
            IntPtr* devices = stackalloc IntPtr[MaximumDevices];
            result = enumerateDevices(api, &count, devices);
            if (result != Success) return Error("ctlEnumerateDevices", result);

            IntPtr match = IntPtr.Zero;
            int candidates = 0;
            for (uint index = 0; index < Math.Min(count, MaximumDevices); index++)
            {
                long luid;
                CTLDeviceAdapterProperties properties = new()
                {
                    Size = (uint)sizeof(CTLDeviceAdapterProperties),
                    Version = AdapterPropertiesVersion,
                    DeviceID = &luid,
                    DeviceIDSize = sizeof(long)
                };
                if (getProperties(devices[index], &properties) != Success
                    || properties.PCIVendorID != GPUVendorFormatting.IntelVendorID)
                    continue;

                if (target.Address.HasValue
                    && properties.Bus == target.Address.Bus
                    && properties.Device == target.Address.Device
                    && properties.Function == target.Address.Function)
                {
                    match = devices[index];
                    candidates = 1;
                    break;
                }

                match = devices[index];
                candidates++;
            }

            if (candidates != 1) return GPUPowerLimitReader.Failure("IGCL did not report this GPU.");

            uint domainCount = MaximumPowerDomains;
            IntPtr* domains = stackalloc IntPtr[MaximumPowerDomains];
            result = enumeratePowerDomains(match, &domainCount, domains);
            if (result != Success || domainCount == 0) return Error("ctlEnumPowerDomains", result);

            for (uint index = 0; index < Math.Min(domainCount, MaximumPowerDomains); index++)
            {
                CTLPowerProperties power = new() { Size = (uint)sizeof(CTLPowerProperties) };
                if (getPowerProperties(domains[index], &power) == Success && power.DefaultLimit > 0)
                    return new GPUPowerLimitResult(Succeeded: true, (uint)power.DefaultLimit, string.Empty);
            }

            return GPUPowerLimitReader.Failure("IGCL does not know this GPU's default power limit.");
        }
        finally
        {
            _ = close(api);
        }
    }

    private static GPUPowerLimitResult Error(string function, int result) =>
        GPUPowerLimitReader.Failure(string.Concat(
            function,
            " failed with IGCL result 0x",
            result.ToString("X8", CultureInfo.InvariantCulture),
            "."));

    [StructLayout(LayoutKind.Sequential)]
    private struct CTLInitArgs
    {
        public uint Size;
        public byte Version;
        public uint AppVersion;
        public uint Flags;
        public uint SupportedVersion;
        public Guid ApplicationUID;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CTLDeviceAdapterProperties
    {
        public uint Size;
        public byte Version;
        public void* DeviceID;
        public uint DeviceIDSize;
        public uint DeviceType;
        public uint SupportedSubfunctionFlags;
        public ulong DriverVersion;
        public ulong FirmwareMajorVersion;
        public ulong FirmwareMinorVersion;
        public ulong FirmwareBuildNumber;
        public uint PCIVendorID;
        public uint PCIDeviceID;
        public uint RevisionID;
        public uint EUsPerSubslice;
        public uint SubslicesPerSlice;
        public uint Slices;
        public fixed byte Name[100];
        public uint GraphicsAdapterProperties;
        public uint Frequency;
        public ushort PCISubsystemID;
        public ushort PCISubsystemVendorID;
        public byte Bus;
        public byte Device;
        public byte Function;
        public uint XeCores;
        public fixed byte Reserved[108];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CTLPowerProperties
    {
        public uint Size;
        public byte Version;
        public byte CanControl;
        public int DefaultLimit;
        public int MinimumLimit;
        public int MaximumLimit;
    }
}
