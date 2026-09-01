namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Centralizes conservative infrastructure fences and representative-host exclusions.</summary>
internal static class SemanticProcessInfrastructurePolicy
{
    private static readonly HashSet<string> IsolatedExecutablePaths = CreateIsolatedExecutablePaths();

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
        if (facts.IsCriticalOrProtected) return true;
        if (PseudoProcessNames.Contains(facts.ExecutableName)) return true;
        return facts.ExecutablePath is { Length: > 0 } executablePath
               && IsolatedExecutablePaths.Contains(NormalizePath(executablePath));
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
