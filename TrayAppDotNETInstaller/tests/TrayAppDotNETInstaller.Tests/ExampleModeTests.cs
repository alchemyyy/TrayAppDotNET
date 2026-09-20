using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>
/// Example mode exists so the window can be run on its own. These pin the two things that matter: the
/// command line resolves to the layout it claims, and the simulation reports the same progress shape a real
/// install does without touching anything.
/// </summary>
public sealed class ExampleModeTests
{
    private const string SingleApplicationName = "VolumeTrayAppDotNET";

    [Fact]
    public void IsRequested_IsFalseWithoutTheArgument()
    {
        Assert.False(ExampleMode.IsRequested([], out string? applicationName));
        Assert.Null(applicationName);
        Assert.False(ExampleMode.IsRequested(["--make-installer", "--output", "x"], out applicationName));
        Assert.Null(applicationName);
    }

    [Fact]
    public void IsRequested_WithoutAnApplicationMeansEveryApplication()
    {
        Assert.True(ExampleMode.IsRequested(["--example"], out string? applicationName));
        Assert.Null(applicationName);
        Assert.True(ExampleMode.IsRequested(["--EXAMPLE"], out applicationName));
        Assert.Null(applicationName);
    }

    [Fact]
    public void IsRequested_TakesTheApplicationThatFollows()
    {
        Assert.True(ExampleMode.IsRequested(["--example", SingleApplicationName], out string? applicationName));
        Assert.Equal(SingleApplicationName, applicationName);
    }

    [Fact]
    public void IsRequested_DoesNotMistakeTheNextFlagForAnApplication()
    {
        Assert.True(ExampleMode.IsRequested(["--example", "--example-fail"], out string? applicationName));
        Assert.Null(applicationName);
    }

    [Fact]
    public void IsFailureRequested_MatchesOnlyItsOwnArgument()
    {
        Assert.True(ExampleMode.IsFailureRequested(["--example", "--example-fail"]));
        Assert.False(ExampleMode.IsFailureRequested(["--example"]));
        Assert.False(ExampleMode.IsFailureRequested([]));
    }

    [Fact]
    public void CreateCatalog_WithoutAnApplicationCarriesEveryApplicationIcon()
    {
        using EmbeddedPayloadCatalog catalog = ExampleMode.CreateCatalog(applicationName: null);

        Assert.True(catalog.IsBundle);
        Assert.Equal(InstallerIcons.ApplicationIconNames().Count, catalog.Payloads.Count);
        foreach (EmbeddedPayload payload in catalog.Payloads)
        {
            Assert.Equal(ExampleMode.Version, payload.Version);
            Assert.EndsWith(SolidPayloadArchive.FileExtension, payload.FileName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CreateCatalog_WithAnApplicationCarriesOnlyThatOne()
    {
        using EmbeddedPayloadCatalog catalog = ExampleMode.CreateCatalog(SingleApplicationName);

        Assert.False(catalog.IsBundle);
        Assert.Single(catalog.Payloads);
        Assert.Equal(SingleApplicationName, catalog.Payloads[0].ApplicationName);
    }

    [Fact]
    public void ApplicationIconNames_CoverTheTrayApplicationsAndNotTheSuite()
    {
        IReadOnlyList<string> names = InstallerIcons.ApplicationIconNames();

        Assert.Contains(SingleApplicationName, names);
        Assert.Contains("BatteryTrayAppDotNET", names);
        Assert.DoesNotContain(InstallerIcons.SuiteIconName, names);
    }

    [Fact]
    public async Task RunAsync_ReportsRisingProgressAndFinishesComplete()
    {
        RecordingProgress progress = new();

        InstallOutcome outcome = await ExampleMode.RunAsync(
            CreatePlan(SingleApplicationName), progress, simulateFailure: false, CancellationToken.None);

        Assert.True(outcome.Success, outcome.ErrorMessage);
        Assert.Null(outcome.ErrorMessage);
        Assert.Single(outcome.InstalledExecutables);
        Assert.NotEmpty(progress.Lines);
        Assert.DoesNotContain(progress.Lines, line => line.IsFailure);
        Assert.Equal(InstallProgressLine.CompletePercent, progress.Lines[^1].Percent);

        int previousPercent = 0;
        foreach (InstallProgressLine line in progress.Lines)
        {
            Assert.True(line.Percent >= previousPercent, $"Progress went backwards to {line.Percent}.");
            previousPercent = line.Percent;
        }
    }

    [Fact]
    public async Task RunAsync_FailsPartwayWhenAskedTo()
    {
        RecordingProgress progress = new();

        InstallOutcome outcome = await ExampleMode.RunAsync(
            CreatePlan(SingleApplicationName), progress, simulateFailure: true, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.ErrorMessage);
        Assert.Empty(outcome.InstalledExecutables);
        Assert.True(progress.Lines[^1].IsFailure);
        // The run has to get far enough to be worth looking at, but never reach the end
        Assert.NotEmpty(progress.Lines);
        Assert.DoesNotContain(progress.Lines, line => !line.IsFailure && line.Percent >= InstallProgressLine.CompletePercent);
    }

    [Fact]
    public async Task RunAsync_ReportsCancellationWithoutThrowing()
    {
        RecordingProgress progress = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        InstallOutcome outcome = await ExampleMode.RunAsync(
            CreatePlan(SingleApplicationName), progress, simulateFailure: false, cancellation.Token);

        Assert.False(outcome.Success);
        Assert.True(progress.Lines[^1].IsFailure);
    }

    [Fact]
    public async Task RunAsync_RejectsAPlanWithNoPayloads()
    {
        RecordingProgress progress = new();
        InstallPlan plan = new(
            InstallMode.Portable,
            TargetDirectory: @"C:\example",
            Payloads: [],
            CreateDesktopShortcut: false,
            CreateStartMenuShortcut: false);

        InstallOutcome outcome = await ExampleMode.RunAsync(plan, progress, simulateFailure: false, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.True(progress.Lines[^1].IsFailure);
    }

    private static InstallPlan CreatePlan(string applicationName)
    {
        using EmbeddedPayloadCatalog catalog = ExampleMode.CreateCatalog(applicationName);
        return new InstallPlan(
            InstallMode.Portable,
            TargetDirectory: @"C:\example",
            catalog.Payloads,
            CreateDesktopShortcut: false,
            CreateStartMenuShortcut: true);
    }

    /// <summary>Collects every reported line so the order and the end state can be asserted.</summary>
    private sealed class RecordingProgress : IProgress<InstallProgressLine>
    {
        public List<InstallProgressLine> Lines { get; } = [];

        public void Report(InstallProgressLine value) => Lines.Add(value);
    }
}
