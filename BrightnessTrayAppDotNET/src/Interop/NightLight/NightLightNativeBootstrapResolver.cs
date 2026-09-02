namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Resolves the exact SettingsHandlers_Display image identity and BlueLightSingleton RVAs for native helpers.
/// </summary>
internal static class NightLightNativeBootstrapResolver
{
    public const string InitializeSymbol = "BlueLightSingleton::Initialize";
    public const string SInstanceSymbol = "BlueLightSingleton::s_instance";
    public const string SetTargetColorTemperatureSymbol = "BlueLightSingleton::SetTargetColorTemperature";

    public const string SetPreviewColorTemperatureChangesSymbol =
        "BlueLightSingleton::SetPreviewColorTemperatureChanges";

    public const string SetBlueLightActiveSymbol = "BlueLightSingleton::SetBlueLightActive";

    private static readonly string SettingsHandlersDLLPath =
        Path.Combine(Environment.SystemDirectory, path2: "SettingsHandlers_Display.dll");

    private static readonly string[] RequiredSymbols =
    [
        InitializeSymbol,
        SInstanceSymbol,
        SetTargetColorTemperatureSymbol,
        SetPreviewColorTemperatureChangesSymbol,
        SetBlueLightActiveSymbol
    ];

    private static readonly Lock ResolutionGate = new();
    private static string? _cachedDLLPath;
    private static NightLightNativeBootstrapDescriptor? _cachedDescriptor;

    /// <summary>
    /// Gets the process-wide descriptor, resolving symbols only when the backing Windows image identity changes.
    /// </summary>
    public static bool TryResolve(out NightLightNativeBootstrapDescriptor descriptor) =>
        TryResolve(SettingsHandlersDLLPath, out descriptor);

    /// <summary>
    /// Invalidates one rejected cached descriptor and performs one bounded re-resolution attempt.
    /// Callers must not loop this method when the replacement helper rejects the refreshed descriptor.
    /// </summary>
    public static bool TryRefreshAfterImageMismatch(
        NightLightNativeBootstrapDescriptor rejectedDescriptor,
        out NightLightNativeBootstrapDescriptor descriptor)
    {
        lock (ResolutionGate)
        {
            if (_cachedDescriptor == rejectedDescriptor)
            {
                _cachedDLLPath = null;
                _cachedDescriptor = null;
            }
        }

        return TryResolve(out descriptor);
    }

    internal static bool TryResolve(
        string dllPath,
        out NightLightNativeBootstrapDescriptor descriptor)
    {
        descriptor = default;
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath)) return false;

        string fullDLLPath;
        try
        {
            fullDLLPath = Path.GetFullPath(dllPath);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightNativeBootstrapResolver: invalid DLL path '{dllPath}': {ex.Message}");
            return false;
        }

        if (!PDBSymbolResolver.TryReadImageIdentity(fullDLLPath, out PDBImageIdentity currentIdentity))
            return false;

        lock (ResolutionGate)
        {
            if (_cachedDescriptor is { } cachedDescriptor
                && string.Equals(_cachedDLLPath, fullDLLPath, StringComparison.OrdinalIgnoreCase)
                && cachedDescriptor.HasImageIdentity(
                    currentIdentity.PDBGuid,
                    currentIdentity.PDBAge,
                    currentIdentity.ImageSize))
            {
                descriptor = cachedDescriptor;
                return true;
            }

            if (!PDBSymbolResolver.TryResolveSymbolsFromImageFile(
                    fullDLLPath,
                    RequiredSymbols,
                    out PDBImageIdentity resolvedIdentity,
                    out Dictionary<string, int> rvas)) return false;

            if (!TryCreateDescriptor(resolvedIdentity, rvas, out descriptor)) return false;

            _cachedDLLPath = fullDLLPath;
            _cachedDescriptor = descriptor;
            return true;
        }
    }

    internal static bool TryCreateDescriptor(
        PDBImageIdentity imageIdentity,
        IReadOnlyDictionary<string, int> rvas,
        out NightLightNativeBootstrapDescriptor descriptor)
    {
        descriptor = default;
        if (imageIdentity.PDBGuid == Guid.Empty
            || imageIdentity.PDBAge == 0
            || imageIdentity.ImageSize == 0
            || !TryGetRVA(rvas, InitializeSymbol, out uint initializeRVA)
            || !TryGetRVA(rvas, SInstanceSymbol, out uint sInstanceRVA)
            || !TryGetRVA(rvas, SetTargetColorTemperatureSymbol, out uint setTargetColorTemperatureRVA)
            || !TryGetRVA(
                rvas,
                SetPreviewColorTemperatureChangesSymbol,
                out uint setPreviewColorTemperatureChangesRVA)
            || !TryGetRVA(rvas, SetBlueLightActiveSymbol, out uint setBlueLightActiveRVA)) return false;

        try
        {
            descriptor = new NightLightNativeBootstrapDescriptor(
                imageIdentity.PDBGuid,
                imageIdentity.PDBAge,
                imageIdentity.ImageSize,
                initializeRVA,
                sInstanceRVA,
                setTargetColorTemperatureRVA,
                setPreviewColorTemperatureChangesRVA,
                setBlueLightActiveRVA);
            return true;
        }
        catch (ArgumentException ex)
        {
            TADNLog.Log($"NightLightNativeBootstrapResolver: invalid resolved descriptor: {ex.Message}");
            return false;
        }
    }

    private static bool TryGetRVA(
        IReadOnlyDictionary<string, int> rvas,
        string symbol,
        out uint RVA)
    {
        RVA = 0;
        if (!rvas.TryGetValue(symbol, out int resolvedRVA) || resolvedRVA <= 0) return false;

        RVA = (uint)resolvedRVA;
        return true;
    }
}
