using System.Globalization;
using TrayAppDotNETInstaller.Localization;

namespace TrayAppDotNETInstaller.Services;

/// <summary>
/// A stand-in for a real installation so the window can be run and reworked on its own, without building a
/// release or stamping an installer. It fabricates a catalog from the icons the factory already carries and
/// simulates a run against the same progress vocabulary the engine uses.
///
/// Nothing is extracted, no child installer starts, no shortcut or registry key is written, and no
/// application is launched at the end. The only thing this shares with a real run is the interface.
///
/// It is reached by "--example [ApplicationName]", which the project's launch profile passes, and, in a
/// Debug build only, by starting an installer that carries no payload at all. A stamped installer is a
/// Release build and always carries a payload, so it can never land here.
/// </summary>
public static class ExampleMode
{
    public const string Argument = "--example";
    public const string FailureArgument = "--example-fail";

    /// <summary>The version the fabricated payloads report, so the header has something to show.</summary>
    public const int Version = 210;

    // Matches InstallEngine, so the bar moves the way it does during a real install
    private const int ExtractPortionEndPercent = 40;
    private const int FullInnerPercent = InstallProgressLine.CompletePercent;
    private const int EntryDelayMilliseconds = 110;
    private const int StageDelayMilliseconds = 450;
    private const int SimulatedFailureExitCode = 1;
    private const char ArgumentPrefix = '-';

    // Which fabricated applications report an existing installation other than a System one
    private const int LocalInstallationIndex = 1;
    private const int NotInstalledIndex = 3;

    // The files a release package actually holds, so the extraction lines look like the real ones
    private static string[] SharedEntryNames { get; } =
    [
        "libSkiaSharp.dll",
        "av_libglesv2.dll",
        "libHarfBuzzSharp.dll",
        "LICENSE.txt",
        "NOTICE"
    ];

    /// <summary>
    /// True when the command line asks for the example window. <paramref name="applicationName"/> receives
    /// the optional application that follows the argument; null means every application, which is the
    /// bundle layout.
    /// </summary>
    public static bool IsRequested(string[] args, out string? applicationName)
    {
        FrameworkCompatibility.ThrowIfNull(args, nameof(args));

        applicationName = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], Argument, StringComparison.OrdinalIgnoreCase)) continue;

            if (index + 1 < args.Length && args[index + 1].Length > 0 && args[index + 1][0] != ArgumentPrefix)
                applicationName = args[index + 1];

            return true;
        }

        return false;
    }

    /// <summary>True when the simulated run should fail partway, which is the only way to reach that state.</summary>
    public static bool IsFailureRequested(string[] args)
    {
        FrameworkCompatibility.ThrowIfNull(args, nameof(args));

        foreach (string argument in args)
        {
            if (string.Equals(argument, FailureArgument, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a catalog of fabricated payloads. A null or empty <paramref name="applicationName"/> yields
    /// every application the factory carries an icon for, which is the bundle layout; anything else yields
    /// that one application, even a name the factory does not know, so an arbitrary name can be tried.
    /// </summary>
    public static EmbeddedPayloadCatalog CreateCatalog(string? applicationName)
    {
        List<string> fileNames = [];
        foreach (string name in ResolveApplicationNames(applicationName))
            fileNames.Add($"{name}_{Version.ToString(CultureInfo.InvariantCulture)}{SolidPayloadArchive.FileExtension}");

        InstallerLog.Write($"ExampleMode: fabricated {fileNames.Count} payload(s)");
        return EmbeddedPayloadCatalog.FromFileNames(fileNames);
    }

    /// <summary>
    /// Fabricates installations already on the machine, so the installed notice, the row labels and the preset
    /// installation type show without installing anything first. Every application is installed for all users
    /// except the second, installed for the current user, and the fourth, not installed, so a suite shows each
    /// kind of row and a single application starts on System.
    /// </summary>
    public static IReadOnlyList<DetectedInstallation> CreateDetectedInstallations(EmbeddedPayloadCatalog catalog)
    {
        FrameworkCompatibility.ThrowIfNull(catalog, nameof(catalog));

        List<DetectedInstallation> installations = [];
        for (int index = 0; index < catalog.Payloads.Count; index++)
        {
            if (index == NotInstalledIndex) continue;

            InstallMode mode = index == LocalInstallationIndex ? InstallMode.Local : InstallMode.System;
            installations.Add(new DetectedInstallation(
                catalog.Payloads[index].ApplicationName,
                mode,
                InstallDefaults.DefaultDirectory(mode, catalog)));
        }

        return installations;
    }

    /// <summary>
    /// Walks the same progress stages and application states a real install reports, on a timer.
    /// Honours cancellation so the window behaves the way it does when a real run is interrupted.
    /// A simulated failure lands on the middle application,
    /// so a suite run leaves applications installed before it and not installed after it.
    /// </summary>
    public static async Task<InstallOutcome> RunAsync(
        InstallPlan plan,
        IProgress<InstallProgressLine> progress,
        IProgress<InstallApplicationStatus> applicationProgress,
        bool simulateFailure,
        CancellationToken cancellationToken)
    {
        FrameworkCompatibility.ThrowIfNull(plan, nameof(plan));
        FrameworkCompatibility.ThrowIfNull(progress, nameof(progress));
        FrameworkCompatibility.ThrowIfNull(applicationProgress, nameof(applicationProgress));

        List<string> installedExecutables = [];
        if (plan.Payloads.Count == 0)
        {
            return InstallEngine.Fail(
                L(nameof(AppStrings.Installer_Progress_NoPayloads)),
                failedApplicationName: null,
                progress,
                applicationProgress,
                installedExecutables);
        }

        InstallerLog.Write($"ExampleMode: simulating a {plan.Mode} install of {plan.Payloads.Count} app(s); nothing is written");
        int failingIndex = plan.Payloads.Count / 2;
        // The application in flight, so an interruption is pinned to it the way the engine pins one
        string? currentApplicationName = null;
        try
        {
            for (int index = 0; index < plan.Payloads.Count; index++)
            {
                // Checked before the next application is reported, as the engine does
                // A run stopped between applications therefore blames none of them
                cancellationToken.ThrowIfCancellationRequested();
                EmbeddedPayload payload = plan.Payloads[index];
                ProgressSlice slice = ProgressSlice.ForIndex(index, plan.Payloads.Count);
                currentApplicationName = payload.ApplicationName;
                applicationProgress.Report(new InstallApplicationStatus(payload.ApplicationName, InstallApplicationState.Installing));
                await SimulateExtractionAsync(
                    payload, slice.Portion(fromInnerPercent: 0, ExtractPortionEndPercent), progress, cancellationToken)
                    .ConfigureAwait(true);

                ProgressSlice installSlice = slice.Portion(ExtractPortionEndPercent, FullInnerPercent);
                progress.Report(InstallProgressLine.At(
                    installSlice.StartPercent,
                    Format(nameof(AppStrings.Installer_Progress_Installing_Format), payload.ApplicationName)));
                await Task.Delay(StageDelayMilliseconds, cancellationToken).ConfigureAwait(true);

                if (simulateFailure && index == failingIndex)
                {
                    return InstallEngine.Fail(
                        Format(
                            nameof(AppStrings.Installer_Error_AppInstallerExitCode_Format),
                            payload.ApplicationName,
                            SimulatedFailureExitCode),
                        payload.ApplicationName,
                        progress,
                        applicationProgress,
                        installedExecutables);
                }

                progress.Report(InstallProgressLine.At(installSlice.EndPercent, DescribeInstalled(plan, payload)));
                installedExecutables.Add(
                    InstallDefaults.InstalledExecutablePath(plan.Mode, plan.TargetDirectory, payload.ApplicationName));
                applicationProgress.Report(new InstallApplicationStatus(payload.ApplicationName, InstallApplicationState.Installed));
                currentApplicationName = null;
            }

            progress.Report(InstallProgressLine.At(
                InstallProgressLine.CompletePercent, L(nameof(AppStrings.Installer_Progress_Complete))));
            return new InstallOutcome(Success: true, ErrorMessage: null, installedExecutables);
        }
        catch (OperationCanceledException)
        {
            return InstallEngine.Fail(
                L(nameof(AppStrings.Installer_Progress_Cancelled)),
                currentApplicationName,
                progress,
                applicationProgress,
                installedExecutables);
        }
    }

    private static async Task SimulateExtractionAsync(
        EmbeddedPayload payload,
        ProgressSlice slice,
        IProgress<InstallProgressLine> progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> entryNames = EntryNamesFor(payload.ApplicationName);
        for (int entryIndex = 0; entryIndex < entryNames.Count; entryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(InstallProgressLine.At(
                slice.Map(entryIndex * FullInnerPercent / entryNames.Count),
                Format(
                    nameof(AppStrings.Installer_Progress_Extracting_Format),
                    payload.ApplicationName,
                    entryNames[entryIndex])));
            await Task.Delay(EntryDelayMilliseconds, cancellationToken).ConfigureAwait(true);
        }

        progress.Report(InstallProgressLine.At(
            slice.EndPercent, Format(nameof(AppStrings.Installer_Progress_Extracted_Format), payload.ApplicationName)));
    }

    /// <summary>The executable first, then the shared native libraries a release package carries.</summary>
    private static IReadOnlyList<string> EntryNamesFor(string applicationName)
    {
        List<string> entryNames = [applicationName + InstallDefaults.ExecutableExtension];
        entryNames.AddRange(SharedEntryNames);
        return entryNames;
    }

    private static IReadOnlyList<string> ResolveApplicationNames(string? applicationName)
    {
        if (!FrameworkCompatibility.IsNullOrWhiteSpace(applicationName)) return [applicationName.Trim()];

        IReadOnlyList<string> iconNames = InstallerIcons.ApplicationIconNames();
        if (iconNames.Count > 0) return iconNames;

        // A build with no application icons still has to show something
        InstallerLog.Write("ExampleMode: the assembly carries no application icons; falling back to the suite name");
        return [InstallerIcons.SuiteIconName];
    }

    private static string DescribeInstalled(InstallPlan plan, EmbeddedPayload payload) =>
        Format(
            nameof(AppStrings.Installer_Progress_Installing_Format),
            payload.ApplicationName + " -> " + InstallDefaults.InstalledExecutablePath(
                plan.Mode, plan.TargetDirectory, payload.ApplicationName));

    private static string L(string key) => LocalizationManager.Instance[key];

    private static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, L(key), arguments);
}
