namespace TrayAppDotNETCommon;

public enum InstallScope
{
    LocalAppData,
    ProgramFiles,
    WindowsStore,
}

/// <summary>
/// Single source of truth for <c>--scope</c> / <c>--remove-scope</c> string mapping.
/// </summary>
public static class InstallScopeExtensions
{
    public const string UserArg = "user";
    public const string SystemArg = "system";
    public const string StoreArg = "store";

    public static string ToArg(InstallScope scope) => scope switch
    {
        InstallScope.LocalAppData => UserArg,
        InstallScope.ProgramFiles => SystemArg,
        InstallScope.WindowsStore => StoreArg,
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    public static InstallScope? ParseArg(string? raw) => raw?.ToLowerInvariant() switch
    {
        UserArg or "local" or "localappdata" => InstallScope.LocalAppData,
        SystemArg or "programfiles" => InstallScope.ProgramFiles,
        StoreArg or "windowsstore" => InstallScope.WindowsStore,
        _ => null,
    };
}
