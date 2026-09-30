namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// Centralizes conservative infrastructure fences, representative-host exclusions and the Windows processes rule.
/// </summary>
internal static class SemanticProcessInfrastructurePolicy
{
    private static readonly HashSet<string> IsolatedExecutablePaths = CreateIsolatedExecutablePaths();
    private static readonly HashSet<string> WindowsProcessExecutablePaths = CreateWindowsProcessExecutablePaths();

    private static readonly HashSet<string> PseudoProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Memory Compression",
        "Registry",
        "Secure System",
        "System",
        "System Idle Process"
    };

    private static readonly HashSet<string> BrokerAndHostNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost.exe",
        "backgroundTaskHost.exe",
        "conhost.exe",
        "dllhost.exe",
        "RuntimeBroker.exe",
        "svchost.exe",
        "taskhostw.exe"
    };

    public static bool IsIsolatedInfrastructure(ProcessGroupingFacts facts)
    {
        if (facts.InstanceKey.ProcessID is 0 or 4) return true;
        if (facts.IsCritical || facts.IsProtected) return true;
        if (PseudoProcessNames.Contains(facts.ExecutableName)) return true;
        return facts.ExecutablePath is { Length: > 0 } executablePath
               && IsolatedExecutablePaths.Contains(NormalizePath(executablePath));
    }

    /// <summary>
    /// Mirrors WdcApplicationsMonitor::IsCriticalProcess, the rule behind Task Manager's Windows processes group,
    /// plus Explorer, which Task Manager files there while it has no window. The caller gives app windows precedence.
    /// </summary>
    public static bool IsWindowsProcess(ProcessGroupingFacts facts)
    {
        // Idle and System, whose operations Task Manager disables
        if (facts.InstanceKey.ProcessID is 0 or 4) return true;
        // The kernel's System, Secure System, Memory Compression and Registry process classifications
        if (PseudoProcessNames.Contains(facts.ExecutableName)) return true;
        if (facts.IsCritical) return true;
        return facts.ExecutablePath is { Length: > 0 } executablePath
               && WindowsProcessExecutablePaths.Contains(NormalizePath(executablePath));
    }

    public static bool IsBrokerOrHost(string executableName) =>
        BrokerAndHostNames.Contains(executableName);

    private static HashSet<string> CreateIsolatedExecutablePaths()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(windowsDirectory)) return paths;

        paths.Add(NormalizePath(Path.Combine(windowsDirectory, "explorer.exe")));
        string systemDirectory = Environment.SystemDirectory;
        string[] executableNames =
        [
            "csrss.exe",
            "dwm.exe",
            "lsass.exe",
            "services.exe",
            "smss.exe",
            "WerFault.exe",
            "WerFaultSecure.exe",
            "wininit.exe",
            "winlogon.exe"
        ];
        for (int executableIndex = 0; executableIndex < executableNames.Length; executableIndex++)
            paths.Add(NormalizePath(Path.Combine(systemDirectory, executableNames[executableIndex])));
        return paths;
    }

    /// <summary>Expands Taskmgr.exe's TmSpecialProcesses::CriticalProcessPaths table, plus Explorer.</summary>
    private static HashSet<string> CreateWindowsProcessExecutablePaths()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windowsDirectory))
            paths.Add(NormalizePath(Path.Combine(windowsDirectory, "explorer.exe")));

        string systemDirectory = Environment.SystemDirectory;
        string[] systemExecutableNames =
        [
            "winlogon.exe",
            "wininit.exe",
            "csrss.exe",
            "lsass.exe",
            "smss.exe",
            "services.exe",
            "taskeng.exe",
            "taskhost.exe",
            "dwm.exe",
            "conhost.exe",
            "svchost.exe",
            "sihost.exe"
        ];
        if (!string.IsNullOrEmpty(systemDirectory))
        {
            for (int executableIndex = 0; executableIndex < systemExecutableNames.Length; executableIndex++)
                paths.Add(NormalizePath(Path.Combine(systemDirectory, systemExecutableNames[executableIndex])));
        }

        // NOTE: current Defender platform updates run from ProgramData, where Task Manager does not match either
        string programFilesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFilesDirectory))
        {
            string defenderDirectory = Path.Combine(programFilesDirectory, "Windows Defender");
            paths.Add(NormalizePath(Path.Combine(defenderDirectory, "MsMpEng.exe")));
            paths.Add(NormalizePath(Path.Combine(defenderDirectory, "NisSrv.exe")));
        }

        return paths;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
        {
            return path;
        }
    }
}
