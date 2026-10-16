using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class InstallProgressAggregatorTests
{
    [Fact]
    public void Report_ShowsTheMeanOfEveryApplicationWithTheLatestMessage()
    {
        List<InstallProgressLine> lines = [];
        InstallProgressAggregator aggregator = new(applicationCount: 4, new ListProgress(lines));

        aggregator.ForApplication(0).Report(InstallProgressLine.At(100, "first done"));
        aggregator.ForApplication(2).Report(InstallProgressLine.At(50, "third halfway"));

        // (100 + 0 + 50 + 0) / 4
        Assert.Equal(InstallProgressLine.At(37, "third halfway"), lines[^1]);
        Assert.Equal(25, lines[0].Percent);
    }

    [Fact]
    public void Report_NeverMovesAnApplicationBackwards()
    {
        List<InstallProgressLine> lines = [];
        InstallProgressAggregator aggregator = new(applicationCount: 2, new ListProgress(lines));
        IProgress<InstallProgressLine> first = aggregator.ForApplication(0);

        first.Report(InstallProgressLine.At(80, "ahead"));
        first.Report(InstallProgressLine.At(20, "late line"));

        Assert.Equal(40, lines[^1].Percent);
        Assert.Equal("late line", lines[^1].Message);
    }

    [Fact]
    public void Report_ReachesCompleteOnlyWhenEveryApplicationHas()
    {
        List<InstallProgressLine> lines = [];
        InstallProgressAggregator aggregator = new(applicationCount: 2, new ListProgress(lines));

        aggregator.ForApplication(0).Report(InstallProgressLine.At(100, "first"));
        Assert.False(lines[^1].IsComplete);

        aggregator.ForApplication(1).Report(InstallProgressLine.At(100, "second"));
        Assert.True(lines[^1].IsComplete);
    }

    [Fact]
    public void Report_DropsFailureLinesBecauseTheEngineReportsTheOutcome()
    {
        List<InstallProgressLine> lines = [];
        InstallProgressAggregator aggregator = new(applicationCount: 2, new ListProgress(lines));

        aggregator.ForApplication(1).Report(InstallProgressLine.Failed("broken"));

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

    private sealed class ListProgress(List<InstallProgressLine> lines) : IProgress<InstallProgressLine>
    {
        public void Report(InstallProgressLine value) => lines.Add(value);
    }
}
