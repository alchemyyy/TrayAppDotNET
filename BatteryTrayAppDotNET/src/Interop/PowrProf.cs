using System.Runtime.InteropServices;

namespace BatteryTrayAppDotNET.Interop;

/// <summary>
/// powrprof.dll power-scheme calls. They take and return scheme GUIDs and raw value indexes, so unlike
/// <c>powercfg</c> output nothing here depends on the Windows display language.
/// </summary>
internal static unsafe class PowrProf
{
    public const uint ErrorSuccess = 0;
    public const uint ErrorMoreData = 234;
    public const uint ErrorNoMoreItems = 259;

    // POWER_DATA_ACCESSOR.ACCESS_SCHEME
    public const uint AccessScheme = 16;

    /// <summary>Returns the active scheme; free <paramref name="activePolicyGuid"/> with LocalFree.</summary>
    [DllImport("powrprof.dll", ExactSpelling = true)]
    public static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out Guid* activePolicyGuid);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    public static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, Guid* schemeGuid);

    /// <summary>Creates a copy of a scheme; with <c>*destinationSchemeGuid</c> null it allocates the new GUID (LocalFree).</summary>
    [DllImport("powrprof.dll", ExactSpelling = true)]
    public static extern uint PowerDuplicateScheme(
        IntPtr rootPowerKey,
        Guid* sourceSchemeGuid,
        Guid** destinationSchemeGuid);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    public static extern uint PowerEnumerate(
        IntPtr rootPowerKey,
        Guid* schemeGuid,
        Guid* subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        byte* buffer,
        uint* bufferSize);

    /// <summary>Reads the scheme's name, resolved to the current display language, as UTF-16 with a terminator.</summary>
    [DllImport("powrprof.dll", ExactSpelling = true)]
    public static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        Guid* schemeGuid,
        Guid* subGroupOfPowerSettingsGuid,
        Guid* powerSettingGuid,
        byte* buffer,
        uint* bufferSize);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    public static extern uint PowerReadACValueIndex(
        IntPtr rootPowerKey,
        Guid* schemeGuid,
        Guid* subGroupOfPowerSettingsGuid,
        Guid* powerSettingGuid,
        uint* acValueIndex);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    public static extern uint PowerReadDCValueIndex(
        IntPtr rootPowerKey,
        Guid* schemeGuid,
        Guid* subGroupOfPowerSettingsGuid,
        Guid* powerSettingGuid,
        uint* dcValueIndex);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    public static extern uint PowerWriteACValueIndex(
        IntPtr rootPowerKey,
        Guid* schemeGuid,
        Guid* subGroupOfPowerSettingsGuid,
        Guid* powerSettingGuid,
        uint acValueIndex);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    public static extern uint PowerWriteDCValueIndex(
        IntPtr rootPowerKey,
        Guid* schemeGuid,
        Guid* subGroupOfPowerSettingsGuid,
        Guid* powerSettingGuid,
        uint dcValueIndex);
}
