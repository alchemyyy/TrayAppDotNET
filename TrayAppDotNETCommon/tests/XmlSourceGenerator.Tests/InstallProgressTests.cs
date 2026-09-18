using System.Diagnostics;
using TrayAppDotNETCommon.Services.Install;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class InstallProgressTests
{
    private const string TestApplicationName = "InstallProgressTests";
    private static readonly TimeSpan PipeCompletionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DisposeCompletionTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public void ToLineAndTryParseLineRoundTripNormalUpdate()
    {
        TrayAppDotNETInstallProgress original = TrayAppDotNETInstallProgress.At(42, "Copying files");

        string line = original.ToLine();
        bool parsed = TrayAppDotNETInstallProgress.TryParseLine(line, out TrayAppDotNETInstallProgress? roundTripped);

        Assert.Equal("[42%] Copying files", line);
        Assert.True(parsed);
        Assert.Equal(original, roundTripped);
        Assert.False(roundTripped!.IsFailure);
        Assert.False(roundTripped.IsComplete);
    }

    [Fact]
    public void CompleteUpdateParsesAsComplete()
    {
        bool parsed = TrayAppDotNETInstallProgress.TryParseLine("[100%] Done", out TrayAppDotNETInstallProgress? progress);

        Assert.True(parsed);
        Assert.True(progress!.IsComplete);
        Assert.Equal(TrayAppDotNETInstallProgress.CompletePercent, progress.Percent);
    }

    [Fact]
    public void AtClampsPercentAboveOneHundred()
    {
        TrayAppDotNETInstallProgress progress = TrayAppDotNETInstallProgress.At(250, "Too far");

        Assert.Equal(TrayAppDotNETInstallProgress.CompletePercent, progress.Percent);
        Assert.Equal("[100%] Too far", progress.ToLine());
    }

    [Fact]
    public void AtClampsPercentBelowZero()
    {
        TrayAppDotNETInstallProgress progress = TrayAppDotNETInstallProgress.At(-5, "Too early");

        Assert.Equal(0, progress.Percent);
        Assert.Equal("[0%] Too early", progress.ToLine());
    }

    [Fact]
    public void ToLineClampsPercentOfDirectlyConstructedRecord()
    {
        TrayAppDotNETInstallProgress progress = new(Percent: 150, "Unclamped");

        Assert.Equal("[100%] Unclamped", progress.ToLine());
    }

    [Fact]
    public void FailureUsesFailTokenAndRoundTrips()
    {
        TrayAppDotNETInstallProgress failure = TrayAppDotNETInstallProgress.Failed("Access denied");

        string line = failure.ToLine();
        bool parsed = TrayAppDotNETInstallProgress.TryParseLine(line, out TrayAppDotNETInstallProgress? roundTripped);

        Assert.Equal("[FAIL] Access denied", line);
        Assert.True(parsed);
        Assert.True(roundTripped!.IsFailure);
        Assert.False(roundTripped.IsComplete);
        Assert.Equal("Access denied", roundTripped.Message);
        Assert.Equal(failure, roundTripped);
    }

    [Fact]
    public void ToLineFlattensMessageNewlines()
    {
        TrayAppDotNETInstallProgress progress = TrayAppDotNETInstallProgress.At(5, "line one\r\nline two\n");

        string line = progress.ToLine();

        Assert.Equal("[5%] line one  line two", line);
        Assert.DoesNotContain("\r", line);
        Assert.DoesNotContain("\n", line);
    }

    [Fact]
    public void TryParseLineToleratesSurroundingWhitespace()
    {
        bool parsed = TrayAppDotNETInstallProgress.TryParseLine("  [7%]   Staging  \r", out TrayAppDotNETInstallProgress? progress);

        Assert.True(parsed);
        Assert.Equal(7, progress!.Percent);
        Assert.Equal("Staging", progress.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"Installed to C:\x")]
    [InlineData("[abc] x")]
    [InlineData("[12] x")]
    [InlineData("[] x")]
    [InlineData("[-5%] x")]
    public void TryParseLineRejectsNonProgressLines(string? line)
    {
        bool parsed = TrayAppDotNETInstallProgress.TryParseLine(line, out TrayAppDotNETInstallProgress? progress);

        Assert.False(parsed);
        Assert.Null(progress);
    }

    [Fact]
    public async Task PipeDeliversReportsInOrderUntilClientDisconnects()
    {
        string pipeName = TrayAppDotNETProgressPipeServer.CreatePipeName(TestApplicationName);
        List<string> logLines = [];
        CollectingProgress target = new();
        TrayAppDotNETInstallProgress[] sent =
        [
            TrayAppDotNETInstallProgress.At(10, "Copying files"),
            TrayAppDotNETInstallProgress.At(55, "Creating shortcuts"),
            TrayAppDotNETInstallProgress.Failed("Access denied")
        ];

        using TrayAppDotNETProgressPipeServer server = new(pipeName, target, logLines.Add);
        Assert.Equal(pipeName, server.PipeName);
        Assert.False(server.HasConnected);

        TrayAppDotNETProgressPipeClient? client = TrayAppDotNETProgressPipeClient.TryConnect(
            pipeName,
            timeoutMilliseconds: 5_000,
            logLines.Add);
        Assert.NotNull(client);

        foreach (TrayAppDotNETInstallProgress progress in sent) client.Report(progress);
        client.Dispose();

        await server.Completion.WaitAsync(PipeCompletionTimeout);

        Assert.True(server.HasConnected);
        Assert.Equal(sent, target.Received);
        Assert.Empty(logLines);
    }

    [Fact]
    public async Task ServerDisposedWithoutClientCompletes()
    {
        string pipeName = TrayAppDotNETProgressPipeServer.CreatePipeName(TestApplicationName);
        List<string> logLines = [];
        CollectingProgress target = new();
        TrayAppDotNETProgressPipeServer server = new(pipeName, target, logLines.Add);

        server.Dispose();
        await server.Completion.WaitAsync(DisposeCompletionTimeout);

        Assert.False(server.HasConnected);
        Assert.Empty(target.Received);
        Assert.Empty(logLines);

        // Second dispose must be a no-op
        server.Dispose();
    }

    [Fact]
    public async Task ServerDisposedFromInsideReportDoesNotStall()
    {
        string pipeName = TrayAppDotNETProgressPipeServer.CreatePipeName(TestApplicationName);
        List<string> logLines = [];
        TrayAppDotNETProgressPipeServer? server = null;
        TimeSpan disposeElapsed = TimeSpan.MaxValue;
        CallbackProgress target = new(progress =>
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            server!.Dispose();
            disposeElapsed = stopwatch.Elapsed;
        });
        server = new TrayAppDotNETProgressPipeServer(pipeName, target, logLines.Add);

        using TrayAppDotNETProgressPipeClient? client = TrayAppDotNETProgressPipeClient.TryConnect(
            pipeName,
            timeoutMilliseconds: 5_000,
            logLines.Add);
        Assert.NotNull(client);
        client.Report(TrayAppDotNETInstallProgress.At(1, "first"));

        await server.Completion.WaitAsync(PipeCompletionTimeout);

        Assert.True(disposeElapsed < TimeSpan.FromSeconds(1), $"Dispose took {disposeElapsed}");
        Assert.DoesNotContain(logLines, line => line.Contains("did not finish", StringComparison.Ordinal));
    }

    [Fact]
    public void ClientTryConnectToMissingPipeReturnsNull()
    {
        string pipeName = TrayAppDotNETProgressPipeServer.CreatePipeName(TestApplicationName + ".Missing");
        List<string> logLines = [];

        TrayAppDotNETProgressPipeClient? client = TrayAppDotNETProgressPipeClient.TryConnect(
            pipeName,
            timeoutMilliseconds: 200,
            logLines.Add);

        Assert.Null(client);
        Assert.Single(logLines);
    }

    [Fact]
    public void CreatePipeNameIsUniquePerCall()
    {
        string first = TrayAppDotNETProgressPipeServer.CreatePipeName(TestApplicationName);
        string second = TrayAppDotNETProgressPipeServer.CreatePipeName(TestApplicationName);

        Assert.StartsWith($"TrayAppDotNET.Progress.{TestApplicationName}.{Environment.ProcessId}.", first);
        Assert.NotEqual(first, second);
    }

    /// <summary>Synchronous IProgress target that runs one callback per report on the reporting thread.</summary>
    private sealed class CallbackProgress(Action<TrayAppDotNETInstallProgress> callback)
        : IProgress<TrayAppDotNETInstallProgress>
    {
        public void Report(TrayAppDotNETInstallProgress value) => callback(value);
    }

    /// <summary>Synchronous IProgress target that records reports in arrival order.</summary>
    private sealed class CollectingProgress : IProgress<TrayAppDotNETInstallProgress>
    {
        private readonly Lock _gate = new();
        private readonly List<TrayAppDotNETInstallProgress> _received = [];

        public TrayAppDotNETInstallProgress[] Received
        {
            get
            {
                lock (_gate) return _received.ToArray();
            }
        }

        public void Report(TrayAppDotNETInstallProgress value)
        {
            lock (_gate) _received.Add(value);
        }
    }
}
