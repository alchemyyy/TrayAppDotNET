using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class InstallProgressAggregatorTests
{
    [Fact]
    public void Report_ShowsTheMeanOfEveryApplicationWithTheLatestMessage()
    {
        List<TrayAppDotNETInstallProgress> lines = [];
        InstallProgressAggregator aggregator = new(applicationCount: 4, new ListProgress(lines));

        aggregator.ForApplication(0).Report(TrayAppDotNETInstallProgress.At(100, "first done"));
        aggregator.ForApplication(2).Report(TrayAppDotNETInstallProgress.At(50, "third halfway"));

        // (100 + 0 + 50 + 0) / 4
        Assert.Equal(TrayAppDotNETInstallProgress.At(37, "third halfway"), lines[^1]);
        Assert.Equal(25, lines[0].Percent);
    }

    [Fact]
    public void Report_NeverMovesAnApplicationBackwards()
    {
        List<TrayAppDotNETInstallProgress> lines = [];
        InstallProgressAggregator aggregator = new(applicationCount: 2, new ListProgress(lines));
        IProgress<TrayAppDotNETInstallProgress> first = aggregator.ForApplication(0);

        first.Report(TrayAppDotNETInstallProgress.At(80, "ahead"));
        first.Report(TrayAppDotNETInstallProgress.At(20, "late line"));

        Assert.Equal(40, lines[^1].Percent);
        Assert.Equal("late line", lines[^1].Message);
    }

    [Fact]
    public void Report_ReachesCompleteOnlyWhenEveryApplicationHas()
    {
        List<TrayAppDotNETInstallProgress> lines = [];
        InstallProgressAggregator aggregator = new(applicationCount: 2, new ListProgress(lines));

        aggregator.ForApplication(0).Report(TrayAppDotNETInstallProgress.At(100, "first"));
        Assert.False(lines[^1].IsComplete);

        aggregator.ForApplication(1).Report(TrayAppDotNETInstallProgress.At(100, "second"));
        Assert.True(lines[^1].IsComplete);
    }

    [Fact]
    public void Report_DropsFailureLinesBecauseTheEngineReportsTheOutcome()
    {
        List<TrayAppDotNETInstallProgress> lines = [];
        InstallProgressAggregator aggregator = new(applicationCount: 2, new ListProgress(lines));

        aggregator.ForApplication(1).Report(TrayAppDotNETInstallProgress.Failed("broken"));

        Assert.Empty(lines);
    }

    [Fact]
    public void Constructor_AndForApplication_RejectOutOfRangeArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstallProgressAggregator(applicationCount: 0, new ListProgress([])));

        InstallProgressAggregator aggregator = new(applicationCount: 2, new ListProgress([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => aggregator.ForApplication(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => aggregator.ForApplication(-1));
    }

    private sealed class ListProgress(List<TrayAppDotNETInstallProgress> lines) : IProgress<TrayAppDotNETInstallProgress>
    {
        public void Report(TrayAppDotNETInstallProgress value) => lines.Add(value);
    }
}
