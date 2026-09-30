using System.IO.Pipes;
using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class ElevatedInstallWorkerTests
{
    private const string FirstApplicationName = "A";
    private const string SecondApplicationName = "B";
    private const string SecondApplicationFailure = "B broke";

    [Fact]
    public void WorkerArguments_RoundTrip()
    {
        WorkerArguments original = new(
            "TrayAppDotNETInstaller.123.abc",
            InstallMode.System,
            CreateDesktopShortcut: true,
            CreateStartMenuShortcut: false,
            ["VolumeTrayAppDotNET", "BatteryTrayAppDotNET"]);

        string[] arguments = ElevatedInstallWorker.BuildWorkerArguments(original);
        bool parsed = ElevatedInstallWorker.TryParseWorkerArguments(arguments, out WorkerArguments? result, out string? error);

        Assert.True(ElevatedInstallWorker.IsWorkerInvocation(arguments));
        Assert.True(parsed);
        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(original.PipeName, result.PipeName);
        Assert.Equal(original.Mode, result.Mode);
        Assert.Equal(original.CreateDesktopShortcut, result.CreateDesktopShortcut);
        Assert.Equal(original.CreateStartMenuShortcut, result.CreateStartMenuShortcut);
        Assert.Equal(original.ApplicationNames, result.ApplicationNames);
    }

    [Fact]
    public void BuildWorkerArguments_UsesTheDocumentedShape()
    {
        string[] arguments = ElevatedInstallWorker.BuildWorkerArguments(new WorkerArguments(
            "pipe", InstallMode.System, CreateDesktopShortcut: false, CreateStartMenuShortcut: true, ["A", "B"]));

        Assert.Equal(
            ["--worker", "--pipe", "pipe", "--mode", "system", "--desktop-shortcut", "false", "--start-menu-shortcut", "true", "--apps", "A,B"],
            arguments);
    }

    [Theory]
    [InlineData("--worker --mode system --desktop-shortcut true --start-menu-shortcut true --apps A")]
    [InlineData("--worker --pipe p --mode remote --desktop-shortcut true --start-menu-shortcut true --apps A")]
    [InlineData("--worker --pipe p --mode system --desktop-shortcut yes --start-menu-shortcut true --apps A")]
    [InlineData("--worker --pipe p --mode system --desktop-shortcut true --start-menu-shortcut true")]
    [InlineData("--worker --pipe p --mode system --desktop-shortcut true --start-menu-shortcut true --apps ,")]
    public void TryParseWorkerArguments_RejectsIncompleteOrInvalidInput(string commandLine)
    {
        // The .NET Framework has no Split(char, StringSplitOptions) overload, so the separator is an array
        string[] arguments = commandLine.Split([' '], StringSplitOptions.RemoveEmptyEntries);

        bool parsed = ElevatedInstallWorker.TryParseWorkerArguments(arguments, out WorkerArguments? result, out string? error);

        Assert.False(parsed);
        Assert.Null(result);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void IsWorkerInvocation_IsFalseWithoutFlag()
    {
        Assert.False(ElevatedInstallWorker.IsWorkerInvocation([]));
        Assert.False(ElevatedInstallWorker.IsWorkerInvocation(["--install-headless", "local"]));
    }

    [Fact]
    public async Task RunWorkerAsync_WritesProgressLinesToPipeAndReturnsSuccess()
    {
        // The .NET Framework has neither IAsyncDisposable nor PipeOptions.CurrentUserOnly. Production pins
        // the pipe to the current user with a PipeSecurity descriptor; this pipe only needs to carry bytes
        // between two tasks in one process, so the plain options are enough.
        string pipeName = $"TrayAppDotNETInstaller.Tests.{FrameworkCompatibility.ProcessID}.{Guid.NewGuid():N}";
        using NamedPipeServerStream server = new(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        WorkerArguments arguments = new(pipeName, InstallMode.System, false, true, ["A"]);

        Task<int> workerTask = ElevatedInstallWorker.RunWorkerAsync(arguments, (progress, _) =>
        {
            progress.Report(InstallProgressLine.At(10, "ten"));
            progress.Report(InstallProgressLine.At(100, "done"));
            return Task.FromResult(new InstallOutcome(Success: true, ErrorMessage: null, []));
        });

        await server.WaitForConnectionAsync();
        List<string> lines = await ReadAllLinesAsync(server);
        int exitCode = await workerTask;

        Assert.Equal(0, exitCode);
        Assert.Equal(["[10%] ten", "[100%] done"], lines);
    }

    [Fact]
    public async Task RunWorkerAsync_AppendsFailureLineWhenEngineReturnsFailureSilently()
    {
        string pipeName = $"TrayAppDotNETInstaller.Tests.{FrameworkCompatibility.ProcessID}.{Guid.NewGuid():N}";
        using NamedPipeServerStream server = new(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        WorkerArguments arguments = new(pipeName, InstallMode.System, false, true, ["A"]);

        Task<int> workerTask = ElevatedInstallWorker.RunWorkerAsync(arguments, static (progress, _) =>
        {
            progress.Report(InstallProgressLine.At(5, "started"));
            return Task.FromResult(new InstallOutcome(Success: false, "boom", []));
        });

        await server.WaitForConnectionAsync();
        List<string> lines = await ReadAllLinesAsync(server);
        int exitCode = await workerTask;

        Assert.Equal(1, exitCode);
        Assert.Equal(["[5%] started", "[FAIL] boom"], lines);
    }

    [Fact]
    public async Task RunWorkerAsync_WritesApplicationStatusLinesBetweenProgressLines()
    {
        string pipeName = $"TrayAppDotNETInstaller.Tests.{FrameworkCompatibility.ProcessID}.{Guid.NewGuid():N}";
        using NamedPipeServerStream server = new(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        WorkerArguments arguments = new(
            pipeName,
            InstallMode.System,
            CreateDesktopShortcut: false,
            CreateStartMenuShortcut: true,
            [FirstApplicationName]);

        Task<int> workerTask = ElevatedInstallWorker.RunWorkerAsync(arguments, static (progress, applicationProgress) =>
        {
            applicationProgress.Report(new InstallApplicationStatus(FirstApplicationName, InstallApplicationState.Installing));
            progress.Report(InstallProgressLine.At(percent: 50, message: "half"));
            applicationProgress.Report(new InstallApplicationStatus(FirstApplicationName, InstallApplicationState.Installed));
            progress.Report(InstallProgressLine.At(percent: 100, message: "done"));
            return Task.FromResult(new InstallOutcome(Success: true, ErrorMessage: null, []));
        });

        await server.WaitForConnectionAsync();
        List<string> lines = await ReadAllLinesAsync(server);
        int exitCode = await workerTask;

        Assert.Equal(expected: 0, exitCode);
        Assert.Equal(["[APP:Installing] A", "[50%] half", "[APP:Installed] A", "[100%] done"], lines);
    }

    [Fact]
    public void RelayWorkerLine_DeliversWhatTheWorkerReportedInTheSameOrder()
    {
        // The lines a worker writes for a run that installs A and then fails on B; the pipe itself is covered above
        List<object> reported =
        [
            new InstallApplicationStatus(FirstApplicationName, InstallApplicationState.Installing),
            InstallProgressLine.At(percent: 25, message: "Installing A"),
            new InstallApplicationStatus(FirstApplicationName, InstallApplicationState.Installed),
            new InstallApplicationStatus(SecondApplicationName, InstallApplicationState.Installing),
            InstallProgressLine.At(percent: 75, message: "Installing B"),
            new InstallApplicationStatus(SecondApplicationName, InstallApplicationState.Failed),
            InstallProgressLine.Failed(SecondApplicationFailure)
        ];
        List<string> lines = [];
        foreach (object report in reported)
        {
            lines.Add(report switch
            {
                InstallProgressLine line => line.ToLine(),
                InstallApplicationStatus status => status.ToLine(),
                _ => throw new InvalidOperationException("Unexpected report type.")
            });
        }

        RelayRecorder recorder = new();
        List<InstallProgressLine> failures = [];
        foreach (string line in lines)
        {
            InstallProgressLine? relayed = ElevatedInstallWorker.RelayWorkerLine(line, recorder, recorder);
            if (relayed?.IsFailure == true) failures.Add(relayed);
        }

        Assert.Equal(reported, recorder.Reports);
        Assert.Equal([InstallProgressLine.Failed(SecondApplicationFailure)], failures);
    }

    [Theory]
    [InlineData("Installed to C:\\x")]
    [InlineData("[APP:Unknown] A")]
    [InlineData("[APP:Installing]")]
    public void RelayWorkerLine_DropsUnrecognizedLines(string line)
    {
        RelayRecorder recorder = new();

        InstallProgressLine? relayed = ElevatedInstallWorker.RelayWorkerLine(line, recorder, recorder);

        Assert.Null(relayed);
        Assert.Empty(recorder.Reports);
    }

    private static async Task<List<string>> ReadAllLinesAsync(Stream stream)
    {
        List<string> lines = [];
        using StreamReader reader = new(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null) lines.Add(line);
        return lines;
    }

    /// <summary>Records progress lines and application statuses in one list, so their relative order can be asserted.</summary>
    private sealed class RelayRecorder : IProgress<InstallProgressLine>, IProgress<InstallApplicationStatus>
    {
        public List<object> Reports { get; } = [];

        public void Report(InstallProgressLine value) => Reports.Add(value);

        public void Report(InstallApplicationStatus value) => Reports.Add(value);
    }
}
