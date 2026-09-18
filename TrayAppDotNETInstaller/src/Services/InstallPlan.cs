namespace TrayAppDotNETInstaller.Services;

/// <summary>Everything the engine needs to perform one installation.</summary>
public sealed record InstallPlan(
    InstallMode Mode,
    string TargetDirectory,
    IReadOnlyList<EmbeddedPayload> Payloads,
    bool CreateDesktopShortcut,
    bool CreateStartMenuShortcut);

/// <summary>Result of one installation. InstalledExecutables lists the apps that completed.</summary>
public sealed record InstallOutcome(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<string> InstalledExecutables);
