using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>
/// The tracker decides what the window shows at the end of each application row.
/// These pin its rules: reports only move an application forward, and the outcome settles whatever the reports left open.
/// </summary>
public sealed class InstallApplicationTrackerTests
{
    private const string FirstApplicationName = "BatteryTrayAppDotNET";
    private const string SecondApplicationName = "BrightnessTrayAppDotNET";
    private const string ThirdApplicationName = "VolumeTrayAppDotNET";
    private const string UnselectedApplicationName = "NetworkTrayAppDotNET";

    [Fact]
    public void NewTracker_StartsEveryApplicationWaiting()
    {
        InstallApplicationTracker tracker = CreateTracker();

        Assert.Equal(
            [InstallApplicationState.Waiting, InstallApplicationState.Waiting, InstallApplicationState.Waiting],
            StatesOf(tracker));
    }

    [Fact]
    public void StateOf_IsNullForAnApplicationOutsideTheRun()
    {
        InstallApplicationTracker tracker = CreateTracker();

        Assert.Null(tracker.StateOf(UnselectedApplicationName));
    }

    [Fact]
    public void Apply_MovesAnApplicationThroughInstallingToInstalled()
    {
        InstallApplicationTracker tracker = CreateTracker();

        Assert.True(tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing)));
        Assert.Equal(InstallApplicationState.Installing, tracker.StateOf(FirstApplicationName));
        Assert.True(tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installed)));

        Assert.Equal(
            [InstallApplicationState.Installed, InstallApplicationState.Waiting, InstallApplicationState.Waiting],
            StatesOf(tracker));
    }

    [Fact]
    public void Apply_IgnoresApplicationsOutsideTheRunAndRepeatedReports()
    {
        InstallApplicationTracker tracker = CreateTracker();

        Assert.False(tracker.Apply(Status(UnselectedApplicationName, InstallApplicationState.Installing)));
        Assert.True(tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing)));
        Assert.False(tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing)));

        Assert.Null(tracker.StateOf(UnselectedApplicationName));
        Assert.Equal(InstallApplicationState.Installing, tracker.StateOf(FirstApplicationName));
    }

    [Fact]
    public void Apply_NeverStepsBackToWaiting()
    {
        InstallApplicationTracker tracker = CreateTracker();
        tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing));

        Assert.False(tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Waiting)));
        Assert.Equal(InstallApplicationState.Installing, tracker.StateOf(FirstApplicationName));
    }

    [Fact]
    public void Apply_LeavesNotInstalledToTheOutcome()
    {
        InstallApplicationTracker tracker = CreateTracker();
        tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing));

        Assert.False(tracker.Apply(Status(FirstApplicationName, InstallApplicationState.NotInstalled)));
        Assert.False(tracker.Apply(Status(SecondApplicationName, InstallApplicationState.NotInstalled)));
        Assert.Equal(InstallApplicationState.Installing, tracker.StateOf(FirstApplicationName));
        Assert.Equal(InstallApplicationState.Waiting, tracker.StateOf(SecondApplicationName));
    }

    [Fact]
    public void Apply_NeverMovesASettledApplication()
    {
        InstallApplicationTracker tracker = CreateTracker();
        tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing));
        tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installed));
        tracker.Apply(Status(SecondApplicationName, InstallApplicationState.Failed));

        Assert.False(tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Failed)));
        Assert.False(tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing)));
        Assert.False(tracker.Apply(Status(SecondApplicationName, InstallApplicationState.Installed)));

        Assert.Equal(InstallApplicationState.Installed, tracker.StateOf(FirstApplicationName));
        Assert.Equal(InstallApplicationState.Failed, tracker.StateOf(SecondApplicationName));
    }

    [Fact]
    public void Apply_MatchesApplicationNamesIgnoringCase()
    {
        InstallApplicationTracker tracker = CreateTracker();

        Assert.True(tracker.Apply(Status(FirstApplicationName.ToUpperInvariant(), InstallApplicationState.Installing)));
        Assert.Equal(InstallApplicationState.Installing, tracker.StateOf(FirstApplicationName));
    }

    [Fact]
    public void Finish_AfterAFailureFailsTheApplicationInFlightAndLeavesTheRestNotInstalled()
    {
        // The elevated worker died while the second application was installing, so nothing reported its failure
        InstallApplicationTracker tracker = CreateTracker();
        tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing));
        tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installed));
        tracker.Apply(Status(SecondApplicationName, InstallApplicationState.Installing));

        tracker.Finish(success: false);

        Assert.Equal(
            [InstallApplicationState.Installed, InstallApplicationState.Failed, InstallApplicationState.NotInstalled],
            StatesOf(tracker));
    }

    [Fact]
    public void Finish_AfterAFailureKeepsTheReportedFailure()
    {
        InstallApplicationTracker tracker = CreateTracker();
        tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing));
        tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Failed));

        tracker.Finish(success: false);

        Assert.Equal(
            [InstallApplicationState.Failed, InstallApplicationState.NotInstalled, InstallApplicationState.NotInstalled],
            StatesOf(tracker));
    }

    [Fact]
    public void Finish_AfterAFailureBeforeAnyReportLeavesEveryApplicationNotInstalled()
    {
        // A declined administrator prompt ends the run before the worker reaches any application
        InstallApplicationTracker tracker = CreateTracker();

        tracker.Finish(success: false);

        Assert.Equal(
            [InstallApplicationState.NotInstalled, InstallApplicationState.NotInstalled, InstallApplicationState.NotInstalled],
            StatesOf(tracker));
    }

    [Fact]
    public void Finish_AfterSuccessInstallsWhateverALostReportLeftOpen()
    {
        InstallApplicationTracker tracker = CreateTracker();
        tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing));
        tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installed));
        tracker.Apply(Status(SecondApplicationName, InstallApplicationState.Installing));

        tracker.Finish(success: true);

        Assert.Equal(
            [InstallApplicationState.Installed, InstallApplicationState.Installed, InstallApplicationState.Installed],
            StatesOf(tracker));
    }

    [Fact]
    public void Apply_AfterFinishChangesNothing()
    {
        // A report the dispatcher delivers after the outcome must not reopen a settled row
        InstallApplicationTracker tracker = CreateTracker();
        tracker.Finish(success: false);

        Assert.False(tracker.Apply(Status(FirstApplicationName, InstallApplicationState.Installing)));
        Assert.False(tracker.Apply(Status(SecondApplicationName, InstallApplicationState.Installed)));

        Assert.Equal(
            [InstallApplicationState.NotInstalled, InstallApplicationState.NotInstalled, InstallApplicationState.NotInstalled],
            StatesOf(tracker));
    }

    private static InstallApplicationTracker CreateTracker() =>
        new([FirstApplicationName, SecondApplicationName, ThirdApplicationName]);

    private static InstallApplicationStatus Status(string applicationName, InstallApplicationState state) =>
        new(applicationName, state);

    /// <summary>The three applications' states in plan order.</summary>
    private static List<InstallApplicationState?> StatesOf(InstallApplicationTracker tracker) =>
    [
        tracker.StateOf(FirstApplicationName),
        tracker.StateOf(SecondApplicationName),
        tracker.StateOf(ThirdApplicationName)
    ];
}
