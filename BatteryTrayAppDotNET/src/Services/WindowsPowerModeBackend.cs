using BatteryTrayAppDotNET.Interop;

namespace BatteryTrayAppDotNET.Services;

/// <summary>
/// Switches Windows power plans and the Energy Saver threshold through powrprof, so nothing depends on the Windows
/// display language. A copy of the hidden Ultimate Performance template gets a new GUID; it is recognized by the
/// GUID this process created or by the friendly name the template itself resolves to in the current language.
/// </summary>
internal static class WindowsPowerModeBackend
{
    private const string UltimateEnglishName = "Ultimate Performance";
    private const uint EnergySaverAlwaysThreshold = 100;
    private const uint EnergySaverNeverThreshold = 0;

    private static readonly Guid UltimateTemplateScheme = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private static readonly Guid BalancedScheme = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid PowerSaverScheme = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    private static readonly Guid EnergySaverSubgroup = new("de830923-a562-41af-a086-e3a2c6bad2da");
    private static readonly Guid EnergySaverBatteryThreshold = new("e69653ca-cf7f-4f05-aa73-cb833fa90ad4");

    // A mode switch and an Energy Saver change each take several calls against the active scheme. One gate keeps
    // them, and the reads that follow them, from interleaving.
    private static readonly SemaphoreSlim Gate = new(initialCount: 1, maxCount: 1);

    // The Ultimate plan this process duplicated, found again even when the user renamed it
    private static Guid? _createdUltimateScheme;

    public static Task<bool?> ReadEnergySaverAsync(CancellationToken cancellationToken) =>
        RunGatedAsync(ReadEnergySaver, cancellationToken);

    /// <summary>Sets Energy Saver to Always or Never on the active scheme.</summary>
    public static Task<bool> ApplyEnergySaverAsync(bool enabled, CancellationToken cancellationToken) =>
        RunGatedAsync(() => ApplyEnergySaver(enabled), cancellationToken);

    public static Task<BatteryPowerModeReadResult> ReadAsync(CancellationToken cancellationToken) =>
        RunGatedAsync(ReadMode, cancellationToken);

    public static Task<bool> ApplyAsync(BatteryPowerMode mode, CancellationToken cancellationToken) =>
        RunGatedAsync(() => ApplyMode(mode), cancellationToken);

    /// <summary>
    /// The Ultimate plan among <paramref name="schemes"/>: the template itself or the copy this process created,
    /// else the first plan named like the template in the current language or in English.
    /// </summary>
    internal static Guid? SelectUltimateScheme(
        IEnumerable<(Guid Scheme, string? Name)> schemes,
        Guid? createdScheme,
        string? localizedUltimateName)
    {
        Guid? namedScheme = null;
        foreach ((Guid scheme, string? name) in schemes)
        {
            if (scheme == UltimateTemplateScheme || scheme == createdScheme) return scheme;
            if (namedScheme == null && IsUltimateName(name, localizedUltimateName)) namedScheme = scheme;
        }

        return namedScheme;
    }

    internal static BatteryPowerMode? ClassifyScheme(
        Guid scheme,
        string? schemeName,
        Guid? createdScheme,
        string? localizedUltimateName)
    {
        if (scheme == BalancedScheme) return BatteryPowerMode.Balanced;
        if (scheme == PowerSaverScheme) return BatteryPowerMode.PowerSaver;
        return SelectUltimateScheme([(scheme, schemeName)], createdScheme, localizedUltimateName) == null
            ? null
            : BatteryPowerMode.Ultimate;
    }

    private static bool IsUltimateName(string? name, string? localizedUltimateName) =>
        !string.IsNullOrWhiteSpace(name)
        && (string.Equals(name, localizedUltimateName, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(name, UltimateEnglishName, StringComparison.OrdinalIgnoreCase));

    private static async Task<T> RunGatedAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The power policy store can be slow to answer; keep it off the caller's (UI) thread
            return await Task.Run(operation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static bool? ReadEnergySaver()
    {
        if (!TryGetActiveScheme(out Guid active)) return null;

        uint? threshold = ReadThreshold(active, ac: false) ?? ReadThreshold(active, ac: true);
        return threshold.HasValue ? threshold.Value >= EnergySaverAlwaysThreshold : null;
    }

    private static bool ApplyEnergySaver(bool enabled)
    {
        if (!TryGetActiveScheme(out Guid active)) return false;

        uint value = enabled ? EnergySaverAlwaysThreshold : EnergySaverNeverThreshold;
        bool acWritten = WriteThreshold(active, ac: true, value);
        bool dcWritten = WriteThreshold(active, ac: false, value);

        // Re-activating the active scheme applies the changed indexes, as powercfg /setactive does
        bool applied = SetActiveScheme(active);
        return acWritten && dcWritten && applied;
    }

    private static BatteryPowerModeReadResult ReadMode()
    {
        if (!TryGetActiveScheme(out Guid active)) return new BatteryPowerModeReadResult(false, null);

        BatteryPowerMode? mode = ClassifyScheme(
            active,
            ReadFriendlyName(active),
            _createdUltimateScheme,
            ReadFriendlyName(UltimateTemplateScheme));
        return new BatteryPowerModeReadResult(true, mode);
    }

    private static bool ApplyMode(BatteryPowerMode mode) =>
        mode switch
        {
            BatteryPowerMode.Ultimate => ApplyUltimate(),
            BatteryPowerMode.Balanced => Activate(BalancedScheme),
            BatteryPowerMode.PowerSaver => Activate(PowerSaverScheme),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    /// <summary>Activates an Ultimate Performance scheme, duplicating the hidden built-in one when none exists.</summary>
    private static bool ApplyUltimate()
    {
        if (FindUltimateScheme() is { } existing && Activate(existing)) return true;

        if (!TryDuplicateScheme(UltimateTemplateScheme, out Guid created)) return false;

        _createdUltimateScheme = created;
        TADNLog.Log($"WindowsPowerModeBackend: created Ultimate Performance plan {created:D}");
        return Activate(created);
    }

    /// <summary>Read-only: the installed Ultimate plan, or null when the template has never been duplicated.</summary>
    internal static Guid? FindUltimateScheme() =>
        SelectUltimateScheme(
            EnumerateSchemes().Select(static scheme => (scheme, ReadFriendlyName(scheme))),
            _createdUltimateScheme,
            ReadFriendlyName(UltimateTemplateScheme));

    private static bool Activate(Guid scheme)
    {
        CarryEnergySaverThreshold(scheme);
        return SetActiveScheme(scheme);
    }

    /// <summary>
    /// Energy Saver's threshold is stored per scheme, so each mode would otherwise switch Energy Saver to its own
    /// setting. Copying the active scheme's threshold keeps Energy Saver where the user left it across mode changes.
    /// </summary>
    private static void CarryEnergySaverThreshold(Guid targetScheme)
    {
        if (!TryGetActiveScheme(out Guid active) || active == targetScheme) return;

        if (ReadThreshold(active, ac: true) is { } acThreshold) _ = WriteThreshold(targetScheme, ac: true, acThreshold);
        if (ReadThreshold(active, ac: false) is { } dcThreshold) _ = WriteThreshold(targetScheme, ac: false, dcThreshold);
    }

    private static unsafe bool TryGetActiveScheme(out Guid scheme)
    {
        uint rc = PowrProf.PowerGetActiveScheme(IntPtr.Zero, out Guid* active);
        if (rc != PowrProf.ErrorSuccess || active == null)
        {
            TADNLog.Log($"WindowsPowerModeBackend: PowerGetActiveScheme failed ({rc})");
            scheme = default;
            return false;
        }

        scheme = *active;
        _ = Kernel32.LocalFree((IntPtr)active);
        return true;
    }

    private static unsafe bool SetActiveScheme(Guid scheme)
    {
        uint rc = PowrProf.PowerSetActiveScheme(IntPtr.Zero, &scheme);
        if (rc == PowrProf.ErrorSuccess) return true;

        TADNLog.Log($"WindowsPowerModeBackend: PowerSetActiveScheme({scheme:D}) failed ({rc})");
        return false;
    }

    private static unsafe bool TryDuplicateScheme(Guid source, out Guid created)
    {
        Guid* destination = null;
        uint rc = PowrProf.PowerDuplicateScheme(IntPtr.Zero, &source, &destination);
        if (rc != PowrProf.ErrorSuccess || destination == null)
        {
            TADNLog.Log($"WindowsPowerModeBackend: PowerDuplicateScheme({source:D}) failed ({rc})");
            created = default;
            return false;
        }

        created = *destination;
        _ = Kernel32.LocalFree((IntPtr)destination);
        return true;
    }

    internal static unsafe List<Guid> EnumerateSchemes()
    {
        List<Guid> schemes = [];
        for (uint index = 0;; index++)
        {
            Guid scheme;
            uint size = (uint)sizeof(Guid);
            uint rc = PowrProf.PowerEnumerate(
                IntPtr.Zero,
                schemeGuid: null,
                subGroupOfPowerSettingsGuid: null,
                PowrProf.AccessScheme,
                index,
                (byte*)&scheme,
                &size);
            if (rc == PowrProf.ErrorNoMoreItems) return schemes;
            if (rc != PowrProf.ErrorSuccess)
            {
                TADNLog.Log($"WindowsPowerModeBackend: PowerEnumerate({index}) failed ({rc})");
                return schemes;
            }

            schemes.Add(scheme);
        }
    }

    internal static unsafe string? ReadFriendlyName(Guid scheme)
    {
        uint size = 0;
        uint rc = PowrProf.PowerReadFriendlyName(IntPtr.Zero, &scheme, null, null, null, &size);
        if (rc is not (PowrProf.ErrorSuccess or PowrProf.ErrorMoreData) || size < sizeof(char)) return null;

        byte[] buffer = new byte[size];
        fixed (byte* name = buffer)
        {
            rc = PowrProf.PowerReadFriendlyName(IntPtr.Zero, &scheme, null, null, name, &size);
            if (rc != PowrProf.ErrorSuccess) return null;

            return new string((char*)name, startIndex: 0, (int)(size / sizeof(char))).TrimEnd('\0');
        }
    }

    private static unsafe uint? ReadThreshold(Guid scheme, bool ac)
    {
        Guid subgroup = EnergySaverSubgroup;
        Guid setting = EnergySaverBatteryThreshold;
        uint value;
        uint rc = ac
            ? PowrProf.PowerReadACValueIndex(IntPtr.Zero, &scheme, &subgroup, &setting, &value)
            : PowrProf.PowerReadDCValueIndex(IntPtr.Zero, &scheme, &subgroup, &setting, &value);
        return rc == PowrProf.ErrorSuccess ? value : null;
    }

    private static unsafe bool WriteThreshold(Guid scheme, bool ac, uint value)
    {
        Guid subgroup = EnergySaverSubgroup;
        Guid setting = EnergySaverBatteryThreshold;
        uint rc = ac
            ? PowrProf.PowerWriteACValueIndex(IntPtr.Zero, &scheme, &subgroup, &setting, value)
            : PowrProf.PowerWriteDCValueIndex(IntPtr.Zero, &scheme, &subgroup, &setting, value);
        if (rc == PowrProf.ErrorSuccess) return true;

        TADNLog.Log($"WindowsPowerModeBackend: writing the Energy Saver threshold to {scheme:D} failed ({rc})");
        return false;
    }
}
