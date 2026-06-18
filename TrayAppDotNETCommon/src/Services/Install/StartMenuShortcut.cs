using Microsoft.Win32;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

public sealed record TrayAppDotNETStartMenuShortcutOptions(
    string ApplicationName,
    TrayAppDotNETInstallLayout Layout,
    Func<IReadOnlyList<TrayAppDotNETInstallationInfo>> DetectInstallations,
    Action<string>? Log = null)
{
    public const string LocalSuffix = "Local";
    public const string SystemSuffix = "System";
    public const string ProgramsProfileRelativePath = @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs";

    public string PlainFileName => $"{ApplicationName}.lnk";

    public string LocalSuffixedFileName => $"{ApplicationName} ({LocalSuffix}).lnk";

    public string SystemSuffixedFileName => $"{ApplicationName} ({SystemSuffix}).lnk";
}

/// <summary>
/// Reconciles per-profile Start Menu Programs shortcuts for local, system, and Store installation states.
/// </summary>
public sealed class TrayAppDotNETStartMenuShortcut(TrayAppDotNETStartMenuShortcutOptions options)
{
    public void Sync(InstallScope? removingScope = null, bool allUsers = false)
    {
        try
        {
            IReadOnlyList<TrayAppDotNETInstallationInfo> infos = options.DetectInstallations();
            bool systemInstalled = removingScope != InstallScope.ProgramFiles
                                   && IsConsideredInstalled(infos, InstallScope.ProgramFiles);
            bool storeInstalled = removingScope != InstallScope.WindowsStore
                                  && IsConsideredInstalled(infos, InstallScope.WindowsStore);
            string systemExe = options.Layout.ProgramFilesInstallExecutable;

            if (!allUsers)
            {
                bool localInstalled = removingScope != InstallScope.LocalAppData
                                      && IsConsideredInstalled(infos, InstallScope.LocalAppData);
                string localExe = options.Layout.LocalAppDataInstallExecutable;
                string programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                ApplyProfile(programsDir, localInstalled, localExe, systemInstalled, systemExe, storeInstalled);
                return;
            }

            foreach (string profile in EnumerateAllProfilePaths())
            {
                try
                {
                    string profilePrograms = Path.Combine(
                        profile,
                        TrayAppDotNETStartMenuShortcutOptions.ProgramsProfileRelativePath);
                    string profileLocalExe = Path.Combine(
                        profile,
                        options.Layout.LocalAppDataExecutableProfileRelativePath);
                    bool profileLocalInstalled = removingScope != InstallScope.LocalAppData
                                                 && File.Exists(profileLocalExe);

                    ApplyProfile(
                        profilePrograms,
                        profileLocalInstalled,
                        profileLocalExe,
                        systemInstalled,
                        systemExe,
                        storeInstalled);
                }
                catch (Exception exProfile)
                {
                    options.Log?.Invoke(
                        $"TrayAppDotNETStartMenuShortcut.Sync (profile {profile}): {exProfile.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartMenuShortcut.Sync: {ex.Message}");
        }
    }

    private static bool IsConsideredInstalled(IReadOnlyList<TrayAppDotNETInstallationInfo> infos, InstallScope scope) =>
        infos.Any(i => i.Scope == scope && i.Status is
            TrayAppDotNETInstallStatus.InstalledUpToDate or
            TrayAppDotNETInstallStatus.InstalledOutOfDate or
            TrayAppDotNETInstallStatus.CurrentlyRunning);

    private void ApplyProfile(
        string programsDir,
        bool localInstalled,
        string localExe,
        bool systemInstalled,
        string systemExe,
        bool storeInstalled)
    {
        int count = (localInstalled ? 1 : 0) + (systemInstalled ? 1 : 0) + (storeInstalled ? 1 : 0);
        bool useSuffixes = count > 1;

        string plainPath = Path.Combine(programsDir, options.PlainFileName);
        string localSuffixedPath = Path.Combine(programsDir, options.LocalSuffixedFileName);
        string systemSuffixedPath = Path.Combine(programsDir, options.SystemSuffixedFileName);

        string? plainTarget = null;
        string? localSuffixedTarget = null;
        string? systemSuffixedTarget = null;

        if (useSuffixes)
        {
            if (localInstalled) localSuffixedTarget = localExe;
            if (systemInstalled) systemSuffixedTarget = systemExe;
        }
        else if (localInstalled)
            plainTarget = localExe;
        else if (systemInstalled) plainTarget = systemExe;

        ApplyDesired(plainPath, plainTarget);
        ApplyDesired(localSuffixedPath, localSuffixedTarget);
        ApplyDesired(systemSuffixedPath, systemSuffixedTarget);
    }

    private void ApplyDesired(string lnkPath, string? targetExe)
    {
        if (targetExe == null) TryDelete(lnkPath);
        else TryCreateShortcut(lnkPath, targetExe);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartMenuShortcut.TryDelete({path}): {ex.Message}");
        }
    }

    private void TryCreateShortcut(string lnkPath, string targetExe)
    {
        try
        {
            string? dir = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Interop.ShellLink.Create(lnkPath, targetExe, options.ApplicationName);
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"TrayAppDotNETStartMenuShortcut.TryCreateShortcut({lnkPath}): {ex.Message}");
        }
    }

    private static IEnumerable<string> EnumerateAllProfilePaths()
    {
        string currentProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return currentProfile;

        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (root != null)
        {
            foreach (string sid in root.GetSubKeyNames())
            {
                if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal)) continue;

                using RegistryKey? sub = root.OpenSubKey(sid);
                if (sub?.GetValue("ProfileImagePath") is not string path) continue;
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;

                if (string.Equals(
                        PathNormalization.Normalize(path),
                        PathNormalization.Normalize(currentProfile),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return path;
            }
        }

        string? defaultProfile = GetDefaultProfilePath();
        if (defaultProfile != null) yield return defaultProfile;
    }

    private static string? GetDefaultProfilePath()
    {
        string current = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? parent = Path.GetDirectoryName(current);
        if (string.IsNullOrEmpty(parent)) return null;

        string defaultProfile = Path.Combine(parent, "Default");
        return Directory.Exists(defaultProfile) ? defaultProfile : null;
    }
}
