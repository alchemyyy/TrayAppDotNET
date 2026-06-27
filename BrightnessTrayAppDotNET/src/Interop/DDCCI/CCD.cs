using System.Runtime.InteropServices;

namespace BrightnessTrayAppDotNET.Interop.DDCCI;

/// <summary>
/// Subset of the Windows Connecting and Configuring Displays (CCD) API
/// used to recover the "Display 1, 2, 3" labels Windows Settings shows.
/// Distinct from the GDI device-name counter (<c>\\.\DISPLAY{n}</c>)
/// that <see cref="User32Monitor.EnumDisplayMonitors"/> hands back:
/// that counter is internal session state, monotonically bumped on every topology event,
/// so after enough hot-plug churn it climbs into the high 20s.
/// Settings instead derives its number from <c>DISPLAYCONFIG_PATH_SOURCE_INFO.id</c> -
/// a per-adapter source index bound to the GPU output port
/// that stays stable across topology shuffles.
/// </summary>
internal static class CCD
{
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    public const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>
    /// Mode-info entries are a tagged union of source / target / desktop-image variants.
    /// We round-trip them opaquely through QueryDisplayConfig,
    /// so the layout just needs the right size (64 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct DISPLAYCONFIG_MODE_INFO_BLOB;

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPaths, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint numModes, [Out] DISPLAYCONFIG_MODE_INFO_BLOB[] modes,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME packet);

    /// <summary>
    /// Maps GDI adapter name (<c>\\.\DISPLAY{n}</c> from <see cref="User32Monitor.GetMonitorInfo"/>)
    /// to the 1-based friendly display number that matches Settings.
    /// Scans only currently-active paths because only those have a GDI source name.
    ///
    /// Returns empty on any CCD failure
    /// - callers should fall back to parsing the trailing digits.
    /// CCD has been stable since Win7, so failures here are vanishingly rare.
    /// </summary>
    public static Dictionary<string, int> BuildFriendlyDisplayNumberMap()
    {
        Dictionary<string, int> map = new(StringComparer.Ordinal);

        int rc = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
        if (rc != 0 || pathCount == 0) return map;

        DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        DISPLAYCONFIG_MODE_INFO_BLOB[] modes = new DISPLAYCONFIG_MODE_INFO_BLOB[modeCount];
        rc = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (rc != 0) return map;

        for (uint i = 0; i < pathCount; i++)
        {
            DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceNameRequest = new()
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                    adapterId = paths[i].sourceInfo.adapterId,
                    id = paths[i].sourceInfo.id,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref sourceNameRequest) != 0) continue;

            string adapter = sourceNameRequest.viewGdiDeviceName;
            if (string.IsNullOrEmpty(adapter)) continue;

            // Settings labels = sourceInfo.id + 1 on a single-adapter machine.
            // On multi-adapter setups source IDs are per-adapter (each starts at 0),
            // so "+1" values can collide;
            // later writes win and the user gets approximately-Settings ordering.
            // Multi-GPU labelling in Settings itself isn't strictly defined,
            // so this is close enough.
            map[adapter] = (int)paths[i].sourceInfo.id + 1;
        }

        return map;
    }

    /// <summary>
    /// Fallback when CCD has no entry for a given adapter:
    /// strips trailing digits off <c>\\.\DISPLAY{n}</c>.
    /// Used to be the primary source of display numbers,
    /// but Windows monotonically bumps <c>n</c> on every new entry
    /// so it stops matching Settings after enough topology churn.
    /// Centralised here so <see cref="DDCCI.DisplayService"/>
    /// and <c>DisplayIdentifierService</c> can't diverge on fallback behaviour.
    /// </summary>
    public static int ParseDisplayNumberFromAdapterName(string? adapterName)
    {
        if (string.IsNullOrEmpty(adapterName)) return 0;

        int i = adapterName.Length - 1;
        while (i >= 0 && char.IsDigit(adapterName[i])) i--;
        string digits = adapterName[(i + 1)..];
        return int.TryParse(digits, out int n) ? n : 0;
    }

    /// <summary>
    /// Combines <see cref="BuildFriendlyDisplayNumberMap"/> with the trailing-digit fallback.
    /// Single entry point for "the same number Settings shows for this adapter."
    /// </summary>
    public static int ResolveFriendlyDisplayNumber(string adapterName, IDictionary<string, int> friendlyByAdapter)
    {
        return friendlyByAdapter.TryGetValue(adapterName, out int n)
            ? n
            : ParseDisplayNumberFromAdapterName(adapterName);
    }
}
