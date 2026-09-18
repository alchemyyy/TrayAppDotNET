using System.IO.Pipes;
using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class ElevatedInstallWorkerTests
{
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

        Task<int> workerTask = ElevatedInstallWorker.RunWorkerAsync(arguments, progress =>
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

        Task<int> workerTask = ElevatedInstallWorker.RunWorkerAsync(arguments, static progress =>
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

    private static async Task<List<string>> ReadAllLinesAsync(Stream stream)
    {
        List<string> lines = [];
        using StreamReader reader = new(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null) lines.Add(line);
        return lines;
    }
}
