namespace TrayAppDotNETCommon.Services.Install;

public sealed record TrayAppDotNETInstallLayout(
    string ApplicationName,
    string SharedRootFolderName,
    string LocalAppDataInstallDirectory,
    string ProgramFilesInstallDirectory,
    string InstalledExecutableFileName)
{
    public string LocalAppDataInstallExecutable =>
        Path.Combine(LocalAppDataInstallDirectory, InstalledExecutableFileName);

    public string ProgramFilesInstallExecutable =>
        Path.Combine(ProgramFilesInstallDirectory, InstalledExecutableFileName);

    public string WindowsAppsRoot { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");

    public string LocalAppDataExecutableProfileRelativePath =>
        Path.Combine("AppData", "Local", SharedRootFolderName, InstalledExecutableFileName);

    public static TrayAppDotNETInstallLayout Create(
        string applicationName,
        string sharedRootFolderName,
        string localAppDataInstallDirectory)
    {
        string installedExecutableFileName = applicationName + ".exe";
        string programFilesInstallDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            sharedRootFolderName);

        return new TrayAppDotNETInstallLayout(
            applicationName,
            sharedRootFolderName,
            localAppDataInstallDirectory,
            programFilesInstallDirectory,
            installedExecutableFileName);
    }
}

public sealed record TrayAppDotNETInstallPayload(
    IReadOnlyList<string> RequiredFileNames,
    IReadOnlyList<string> OptionalFileNames,
    IReadOnlyList<TrayAppDotNETInstallDirectory> RequiredDirectories,
    IReadOnlyList<TrayAppDotNETInstallDirectory> OptionalDirectories)
{
    public static TrayAppDotNETInstallPayload ManagedApp(
        string applicationName,
        IEnumerable<string>? requiredDirectories = null,
        IEnumerable<string>? optionalDirectories = null,
        bool includePdb = true)
    {
        string[] requiredFiles =
        [
            applicationName + ".dll",
            applicationName + ".deps.json",
            applicationName + ".runtimeconfig.json",
        ];

        string[] optionalFiles = includePdb ? [applicationName + ".pdb"] : [];

        return new TrayAppDotNETInstallPayload(
            requiredFiles,
            optionalFiles,
            ToDirectories(requiredDirectories ?? ["runtime"]),
            ToDirectories(optionalDirectories ?? []));
    }

    public static TrayAppDotNETInstallPayload NativeAOTApp(
        string applicationName,
        IEnumerable<string>? requiredDirectories = null,
        IEnumerable<string>? optionalDirectories = null,
        bool includePdb = true)
    {
        string[] requiredFiles =
        [
            "av_libglesv2.dll",
            "libHarfBuzzSharp.dll",
            "libSkiaSharp.dll",
        ];

        string[] optionalFiles = includePdb
            ?
            [
                applicationName + ".pdb",
                "TrayAppDotNETCommon.pdb",
                "libHarfBuzzSharp.pdb",
                "libSkiaSharp.pdb",
                "libMonoPosixHelper.dll",
                "MonoPosixHelper.dll",
            ]
            :
            [
                "libMonoPosixHelper.dll",
                "MonoPosixHelper.dll",
            ];

        return new TrayAppDotNETInstallPayload(
            requiredFiles,
            optionalFiles,
            ToDirectories(requiredDirectories ?? []),
            ToDirectories(optionalDirectories ?? []));
    }

    private static TrayAppDotNETInstallDirectory[] ToDirectories(IEnumerable<string> names) =>
        names.Select(name => new TrayAppDotNETInstallDirectory(name)).ToArray();
}

public sealed record TrayAppDotNETInstallDirectory(
    string Name,
    bool RemoveOnlyWhenInstallRootHasNoExe = true);

public enum TrayAppDotNETInstallStatus
{
    NotInstalled,
    InstalledUpToDate,
    InstalledOutOfDate,
    CurrentlyRunning,
}

public sealed record TrayAppDotNETInstallationInfo(
    InstallScope Scope,
    string InstallExecutablePath,
    TrayAppDotNETInstallStatus Status,
    int? InstalledVersion);

public sealed record TrayAppDotNETInstallResult(
    bool Success,
    string? ErrorMessage = null,
    bool UserCancelled = false);
