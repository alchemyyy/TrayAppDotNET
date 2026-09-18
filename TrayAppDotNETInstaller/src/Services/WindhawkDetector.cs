using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace TrayAppDotNETInstaller.Services;

public sealed record WindhawkDetection(bool IsInstalled, string? Evidence);

/// <summary>A named check that returns human-readable evidence when Windhawk is found, or null.</summary>
public sealed record WindhawkProbe(string Name, Func<string?> Run);

/// <summary>Detects a Windhawk installation through several independent probes; any hit counts.</summary>
public static class WindhawkDetector
{
    private const string WindhawkName = "Windhawk";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string WindhawkSoftwareKeyPath = @"SOFTWARE\Windhawk";
    private const string DisplayNameValueName = "DisplayName";
    private const string WindhawkExecutableName = "windhawk.exe";
    private const string WindhawkProcessName = "windhawk";

    public static WindhawkDetection Detect() => Aggregate(DefaultProbes());

    public static IReadOnlyList<WindhawkProbe> DefaultProbes() =>
    [
        new WindhawkProbe("uninstall registry", ProbeUninstallRegistry),
        new WindhawkProbe("registry key", ProbeSoftwareKey),
        new WindhawkProbe("program files", ProbeProgramFiles),
        new WindhawkProbe("program data", ProbeProgramData),
        new WindhawkProbe("running process", ProbeRunningProcess)
    ];

    /// <summary>Runs the probes in order and reports the first one that yields evidence.</summary>
    public static WindhawkDetection Aggregate(IReadOnlyList<WindhawkProbe> probes)
    {
        FrameworkCompatibility.ThrowIfNull(probes, nameof(probes));

        foreach (WindhawkProbe probe in probes)
        {
            string? evidence;
            try
            {
                evidence = probe.Run();
            }
            catch (Exception exception) when (exception is SecurityException or IOException or UnauthorizedAccessException)
            {
                InstallerLog.Write($"WindhawkDetector: probe '{probe.Name}' failed", exception);
                continue;
            }

            if (evidence != null)
                return new WindhawkDetection(IsInstalled: true, $"{probe.Name}: {evidence}");
        }

        return new WindhawkDetection(IsInstalled: false, Evidence: null);
    }

    private static string? ProbeUninstallRegistry()
    {
        (RegistryHive Hive, RegistryView View)[] roots =
        [
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default)
        ];

        foreach ((RegistryHive hive, RegistryView view) in roots)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? uninstallKey = baseKey.OpenSubKey(UninstallKeyPath);
            if (uninstallKey == null) continue;

            foreach (string subKeyName in uninstallKey.GetSubKeyNames())
            {
                if (subKeyName.StartsWith(WindhawkName, StringComparison.OrdinalIgnoreCase))
                    return $@"{hive}\{UninstallKeyPath}\{subKeyName} ({view})";

                using RegistryKey? productKey = uninstallKey.OpenSubKey(subKeyName);
                if (productKey?.GetValue(DisplayNameValueName) is string displayName &&
                    displayName.StartsWith(WindhawkName, StringComparison.OrdinalIgnoreCase))
                    return $@"{hive}\{UninstallKeyPath}\{subKeyName} DisplayName={displayName} ({view})";
            }
        }

        return null;
    }

    private static string? ProbeSoftwareKey()
    {
        RegistryView[] views = [RegistryView.Registry64, RegistryView.Registry32];
        foreach (RegistryView view in views)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? windhawkKey = baseKey.OpenSubKey(WindhawkSoftwareKeyPath);
            if (windhawkKey != null) return $@"HKLM\{WindhawkSoftwareKeyPath} ({view})";
        }

        return null;
    }

    private static string? ProbeProgramFiles()
    {
        string[] programFilesRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        ];

        foreach (string root in programFilesRoots)
        {
            if (string.IsNullOrEmpty(root)) continue;

            string executablePath = Path.Combine(root, WindhawkName, WindhawkExecutableName);
            if (File.Exists(executablePath)) return executablePath;
        }

        return null;
    }

    private static string? ProbeProgramData()
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrEmpty(programData)) return null;

        string directory = Path.Combine(programData, WindhawkName);
        return Directory.Exists(directory) ? directory : null;
    }

    private static string? ProbeRunningProcess()
    {
        Process[] processes = Process.GetProcessesByName(WindhawkProcessName);
        try
        {
            return processes.Length == 0 ? null : $"{WindhawkProcessName} PID {processes[0].Id}";
        }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }
}
