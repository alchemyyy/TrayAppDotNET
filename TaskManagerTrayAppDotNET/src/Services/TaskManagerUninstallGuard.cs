namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// Keeps an uninstall from deleting the executable the machine-wide taskmgr.exe redirect points at, which would break
/// Ctrl+Shift+Esc and every other Task Manager launch for all accounts. Removing the redirect needs administrator
/// rights; the uninstall helper has them only for a Program Files uninstall (or when the uninstall was started from an
/// elevated process). A stale redirect to a per-user copy, left by an older build, therefore stops the uninstall
/// with a message instead of being left behind: the user turns Replace Windows Task Manager off (which asks for
/// administrator approval through the existing <see cref="TaskManagerReplacement.ConfigureModeArgument"/> path)
/// and uninstalls again. The uninstall never elevates the user-writable per-user executable on its own.
/// </summary>
internal static class TaskManagerUninstallGuard
{
    /// <summary>
    /// Runs in the process that starts the uninstall, before the helper: stops the uninstall when the redirect
    /// points at the copy being removed and the helper will not be able to remove it.
    /// </summary>
    public static string? CheckUninstall(InstallScope scope) =>
        BlocksUninstall(
            TaskManagerReplacement.GetState().RegisteredExecutable,
            AppServices.Install.InstallExecutable(scope),
            HelperIsElevated(scope, Environment.IsPrivilegedProcess))
            ? BlockedMessage
            : null;

    /// <summary>
    /// Runs in the uninstall helper before anything is removed: removes a redirect to the copy being removed, and
    /// stops the uninstall when that removal fails. A system uninstall that goes ahead also deletes every account's
    /// Always run as administrator task, which only ever starts the system install.
    /// </summary>
    public static string? BeforePrepare(InstallScope scope)
    {
        if (!TaskManagerReplacement.RemoveForUninstall(scope)) return BlockedMessage;

        AppServices.ElevatedStart.RemoveForUninstall(scope);
        return null;
    }

    /// <summary>
    /// Whether the uninstall helper runs elevated: a Program Files uninstall always asks for it, and a helper started
    /// from an elevated process inherits its token.
    /// </summary>
    internal static bool HelperIsElevated(InstallScope scope, bool startedElevated) =>
        scope == InstallScope.ProgramFiles || startedElevated;

    /// <summary>
    /// Whether the uninstall must stop: the registered redirect is exactly the executable being removed and the helper
    /// cannot delete the HKLM value.
    /// </summary>
    internal static bool BlocksUninstall(string? registeredExecutable, string removedExecutable, bool helperElevated) =>
        !helperElevated && TaskManagerReplacement.IsOwnedBy(registeredExecutable, [removedExecutable]);

    private static string BlockedMessage =>
        LocalizationManager.Instance[nameof(AppStrings.Uninstall_TaskManagerRedirectBlocks)];
}
