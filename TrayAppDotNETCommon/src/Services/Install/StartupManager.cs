using Microsoft.Win32;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

public sealed record TrayAppDotNETStartupOptions(
    string ApplicationName,
    TrayAppDotNETInstallLayout Layout,
    Func<IReadOnlyList<TrayAppDotNETInstallationInfo>> DetectInstallations,
    Action<string>? Log = null)
{
    public const string DefaultLegacyRunKeyRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string ShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            ApplicationName + ".lnk");

    public string LegacyRunKeyRegistryPath { get; init; } = DefaultLegacyRunKeyRegistryPath;
}

/// <summary>
/// Manages the shell:startup shortcut and legacy Run-key cleanup for a TrayAppDotNET app.
/// </summary>
public sealed class TrayAppDotNETStartupManager(TrayAppDotNETStartupOptions options)
{
    public string ShortcutPath => options.ShortcutPath;

    public string LegacyRunKeyRegistryPath => options.LegacyRunKeyRegistryPath;

    public bool GetRunOnStartup()
    {
        try
        {
            return File.Exists(ShortcutPath);
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartupManager.GetRunOnStartup: {ex.Message}");
            return false;
        }
    }

    public void SetRunOnStartup(bool enabled)
    {
        try
        {
            if (enabled)
            {
                string exe = ResolveStartupTarget();
                if (string.IsNullOrEmpty(exe)) return;
                CreateShortcut(ShortcutPath, exe);
            }
            else if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartupManager.SetRunOnStartup: {ex.Message}");
        }
    }

    public void RetargetShortcutIfPresent(InstallScope? exclude = null)
    {
        try
        {
            if (!File.Exists(ShortcutPath)) return;

            string desired = ResolveStartupTarget(exclude);
            if (string.IsNullOrEmpty(desired)) return;

            string? current = Interop.ShellLink.TryRead(ShortcutPath, options.Log);
            if (!string.IsNullOrEmpty(current)
                && string.Equals(
                    PathNormalization.Normalize(current),
                    PathNormalization.Normalize(desired),
                    StringComparison.OrdinalIgnoreCase))
                return;

            CreateShortcut(ShortcutPath, desired);
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartupManager.RetargetShortcutIfPresent: {ex.Message}");
        }
    }

    public string? GetCurrentShortcutTarget()
    {
        try
        {
            if (!File.Exists(ShortcutPath)) return null;
            string? target = Interop.ShellLink.TryRead(ShortcutPath, options.Log);
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartupManager.GetCurrentShortcutTarget: {ex.Message}");
            return null;
        }
    }

    public void RemoveLegacyRunKey()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(LegacyRunKeyRegistryPath, writable: true);
            if (key?.GetValue(options.ApplicationName) != null)
                key.DeleteValue(options.ApplicationName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartupManager.RemoveLegacyRunKey: {ex.Message}");
        }
    }

    public void RepairShortcutIfStale()
    {
        try
        {
            if (!File.Exists(ShortcutPath)) return;

            string? target = Interop.ShellLink.TryRead(ShortcutPath, options.Log);
            if (IsValidInstallationTarget(target)) return;

            string? runningInstallExecutable = GetRunningInstallExecutablePathOrNull();
            if (runningInstallExecutable != null) CreateShortcut(ShortcutPath, runningInstallExecutable);
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartupManager.RepairShortcutIfStale: {ex.Message}");
        }
    }

    public string ResolveStartupTarget(InstallScope? exclude = null)
    {
        try
        {
            if (exclude != InstallScope.ProgramFiles && File.Exists(options.Layout.ProgramFilesInstallExecutable))
                return options.Layout.ProgramFilesInstallExecutable;

            if (exclude != InstallScope.LocalAppData && File.Exists(options.Layout.LocalAppDataInstallExecutable))
                return options.Layout.LocalAppDataInstallExecutable;

            string? running = Environment.ProcessPath;
            if (string.IsNullOrEmpty(running)) return string.Empty;
            if (!running.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return string.Empty;

            if (exclude.HasValue)
            {
                string excludedExe = exclude.Value == InstallScope.ProgramFiles
                    ? options.Layout.ProgramFilesInstallExecutable
                    : options.Layout.LocalAppDataInstallExecutable;
                if (string.Equals(
                        PathNormalization.Normalize(running),
                        PathNormalization.Normalize(excludedExe),
                        StringComparison.OrdinalIgnoreCase))
                    return string.Empty;
            }

            return running;
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartupManager.ResolveStartupTarget: {ex.Message}");
            return string.Empty;
        }
    }

    private bool IsValidInstallationTarget(string? targetPath)
    {
        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath)) return false;

        string normalized = PathNormalization.Normalize(targetPath);
        return string.Equals(
                   normalized,
                   PathNormalization.Normalize(options.Layout.LocalAppDataInstallExecutable),
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   normalized,
                   PathNormalization.Normalize(options.Layout.ProgramFilesInstallExecutable),
                   StringComparison.OrdinalIgnoreCase);
    }

    private string? GetRunningInstallExecutablePathOrNull()
    {
        try
        {
            return options.DetectInstallations()
                .FirstOrDefault(i => i is
                {
                    Status: TrayAppDotNETInstallStatus.CurrentlyRunning,
                    Scope: InstallScope.LocalAppData or InstallScope.ProgramFiles,
                })
                ?.InstallExecutablePath;
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartupManager.GetRunningInstallExecutablePathOrNull: {ex.Message}");
            return null;
        }
    }

    private void CreateShortcut(string lnkPath, string targetExe)
    {
        string? lnkDir = Path.GetDirectoryName(lnkPath);
        if (!string.IsNullOrEmpty(lnkDir)) Directory.CreateDirectory(lnkDir);
        Interop.ShellLink.Create(lnkPath, targetExe, options.ApplicationName);
    }
}
