using TrayAppDotNETCommon.Models;

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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), path2: "WindowsApps");

    public string LocalAppDataExecutableProfileRelativePath =>
        Path.Combine(path1: "AppData", path2: "Local", SharedRootFolderName, InstalledExecutableFileName);

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
    IReadOnlyList<TrayAppDotNETInstallDirectory> OptionalDirectories,
    bool CopySourceDirectoryRootFiles = false)
{
    private const string LicenseFileName = "LICENSE.txt";
    private const string SourceCodeFileName = "SOURCE_CODE.txt";
    private const string ThirdPartyLicensesDirectoryName = "THIRD_PARTY_LICENSES";
    private const string ThirdPartyNoticesFileName = "THIRD_PARTY_NOTICES.txt";

    public IReadOnlyList<TrayAppDotNETInstallFile> InstalledFiles(string installedExecutableFileName) =>
    [
        new(installedExecutableFileName),
        .. RequiredFiles,
        .. OptionalFiles
    ];

    public IReadOnlyList<TrayAppDotNETInstallDirectory> InstalledDirectories =>
    [
        .. RequiredDirectories,
        .. OptionalDirectories
    ];

    public static TrayAppDotNETInstallPayload ManagedApp(
        string applicationName,
        IEnumerable<string>? requiredDirectories = null,
        IEnumerable<string>? optionalDirectories = null)
    {
        TrayAppDotNETInstallFile[] requiredFiles =
        [
            new(applicationName + ".dll"),
            new(applicationName + ".deps.json"),
            new(applicationName + ".runtimeconfig.json"),
            .. CreateLegalFiles()
        ];
        TrayAppDotNETInstallDirectory[] resolvedRequiredDirectories =
        [
            new(ThirdPartyLicensesDirectoryName, RemoveOnlyWhenInstallRootHasNoExe: true),
            .. ToDirectories(requiredDirectories ?? ["runtime"])
        ];

        return new TrayAppDotNETInstallPayload(
            requiredFiles,
            [],
            resolvedRequiredDirectories,
            ToDirectories(optionalDirectories ?? []));
    }

    public static TrayAppDotNETInstallPayload NativeAOTApp(
        string applicationName,
        IEnumerable<string>? requiredDirectories = null,
        IEnumerable<string>? optionalDirectories = null)
    {
        TrayAppDotNETInstallFile[] requiredFiles =
        [
            new(Name: "av_libglesv2.dll", RemoveOnlyWhenInstallRootHasNoExe: true),
            new(Name: "libHarfBuzzSharp.dll", RemoveOnlyWhenInstallRootHasNoExe: true),
            new(Name: "libSkiaSharp.dll", RemoveOnlyWhenInstallRootHasNoExe: true),
            .. CreateLegalFiles()
        ];

        TrayAppDotNETInstallFile[] optionalFiles =
        [
            new(Name: "libMonoPosixHelper.dll", RemoveOnlyWhenInstallRootHasNoExe: true),
            new(Name: "MonoPosixHelper.dll", RemoveOnlyWhenInstallRootHasNoExe: true)
        ];
        TrayAppDotNETInstallDirectory[] resolvedRequiredDirectories =
        [
            new(ThirdPartyLicensesDirectoryName, RemoveOnlyWhenInstallRootHasNoExe: true),
            .. ToDirectories(requiredDirectories ?? [])
        ];

        return new TrayAppDotNETInstallPayload(
            requiredFiles,
            optionalFiles,
            resolvedRequiredDirectories,
            ToDirectories(optionalDirectories ?? []));
    }

    private static TrayAppDotNETInstallFile[] CreateLegalFiles() =>
    [
        new(LicenseFileName, RemoveOnlyWhenInstallRootHasNoExe: true),
        new(SourceCodeFileName, RemoveOnlyWhenInstallRootHasNoExe: true),
        new(ThirdPartyNoticesFileName, RemoveOnlyWhenInstallRootHasNoExe: true)
    ];

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
    CurrentlyRunning
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
