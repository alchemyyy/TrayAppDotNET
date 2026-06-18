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
    IReadOnlyList<TrayAppDotNETInstallFile> RequiredFiles,
    IReadOnlyList<TrayAppDotNETInstallFile> OptionalFiles,
    IReadOnlyList<TrayAppDotNETInstallDirectory> RequiredDirectories,
    IReadOnlyList<TrayAppDotNETInstallDirectory> OptionalDirectories)
{
    public IReadOnlyList<TrayAppDotNETInstallFile> InstalledFiles(string installedExecutableFileName) =>
    [
        new TrayAppDotNETInstallFile(installedExecutableFileName),
        .. RequiredFiles,
        .. OptionalFiles,
    ];

    public IReadOnlyList<TrayAppDotNETInstallDirectory> InstalledDirectories =>
    [
        .. RequiredDirectories,
        .. OptionalDirectories,
    ];

    public static TrayAppDotNETInstallPayload ManagedApp(
        string applicationName,
        IEnumerable<string>? requiredDirectories = null,
        IEnumerable<string>? optionalDirectories = null,
        bool includePdb = true)
    {
        string[] requiredFileNames =
        [
            applicationName + ".dll",
            applicationName + ".deps.json",
            applicationName + ".runtimeconfig.json",
        ];

        string[] optionalFileNames = includePdb ? [applicationName + ".pdb"] : [];

        return new TrayAppDotNETInstallPayload(
            ToFiles(requiredFileNames),
            ToFiles(optionalFileNames),
            ToDirectories(requiredDirectories ?? ["runtime"]),
            ToDirectories(optionalDirectories ?? []));
    }

    public static TrayAppDotNETInstallPayload NativeAOTApp(
        string applicationName,
        IEnumerable<string>? requiredDirectories = null,
        IEnumerable<string>? optionalDirectories = null,
        bool includePdb = true)
    {
        TrayAppDotNETInstallFile[] requiredFiles =
        [
            new("av_libglesv2.dll", RemoveOnlyWhenInstallRootHasNoExe: true),
            new("libHarfBuzzSharp.dll", RemoveOnlyWhenInstallRootHasNoExe: true),
            new("libSkiaSharp.dll", RemoveOnlyWhenInstallRootHasNoExe: true),
        ];

        TrayAppDotNETInstallFile[] symbolFiles = includePdb
            ?
            [
                new TrayAppDotNETInstallFile(applicationName + ".pdb"),
                new TrayAppDotNETInstallFile("TrayAppDotNETCommon.pdb", RemoveOnlyWhenInstallRootHasNoExe: true),
                new TrayAppDotNETInstallFile("libHarfBuzzSharp.pdb", RemoveOnlyWhenInstallRootHasNoExe: true),
                new TrayAppDotNETInstallFile("libSkiaSharp.pdb", RemoveOnlyWhenInstallRootHasNoExe: true),
            ]
            : [];

        TrayAppDotNETInstallFile[] optionalFiles =
        [
            .. symbolFiles,
            new("libMonoPosixHelper.dll", RemoveOnlyWhenInstallRootHasNoExe: true),
            new("MonoPosixHelper.dll", RemoveOnlyWhenInstallRootHasNoExe: true),
        ];

        return new TrayAppDotNETInstallPayload(
            requiredFiles,
            optionalFiles,
            ToDirectories(requiredDirectories ?? []),
            ToDirectories(optionalDirectories ?? []));
    }

    private static TrayAppDotNETInstallFile[] ToFiles(IEnumerable<string> names) =>
        names.Select(name => new TrayAppDotNETInstallFile(name)).ToArray();

    private static TrayAppDotNETInstallDirectory[] ToDirectories(IEnumerable<string> names) =>
        names.Select(name => new TrayAppDotNETInstallDirectory(name)).ToArray();
}

public sealed record TrayAppDotNETInstallFile(
    string Name,
    bool RemoveOnlyWhenInstallRootHasNoExe = false);

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
