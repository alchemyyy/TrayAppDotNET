namespace TrayAppDotNETCommon;

public sealed class TrayAppDotNETHostOptions
{
    public required string ApplicationName { get; init; }
    public string ToolTipText { get; init; } = string.Empty;
    public bool IsUninstallerMode { get; init; }
    public string? UninstallerInstallDir { get; init; }
    public string UninstallerScopeText { get; init; } = string.Empty;
    public Action? InitializeServices { get; init; }
    public Action? ShutdownServices { get; init; }
    public Action? OpenSettingsFolder { get; init; }
    public Func<bool, Task>? RunUninstallAsync { get; init; }
    public Action<string>? Log { get; init; }
}
