namespace TrayAppDotNETInstaller.Services;

/// <summary>
/// Everything the engine needs to perform one installation. A null shortcut choice is passed on as no flag at
/// all, so the app's installer leaves that shortcut as it is.
/// </summary>
public sealed record InstallPlan(
    InstallMode Mode,
    string TargetDirectory,
    IReadOnlyList<EmbeddedPayload> Payloads,
    bool? CreateDesktopShortcut,
    bool? CreateStartMenuShortcut);

/// <summary>
/// Result of one installation. <see cref="InstalledExecutables"/> lists the apps that completed, also when the run
/// as a whole failed, so the window can start again what the run stopped and finished.
/// </summary>
public sealed record InstallOutcome(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<string> InstalledExecutables);
