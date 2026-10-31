using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon;
using TrayAppDotNETCommon.Localization;
using Xunit;
using AppStrings = TaskManagerTrayAppDotNET.Localization.Strings;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ElevationBrokerProtocolTests
{
    [Theory]
    [InlineData(1L, 1, 1234, 638000000000000000L, 32UL, "")]                                // SetPriority, empty text
    [InlineData(9_007_199_254_740_993L, 2, 5, 0L, 18_446_744_073_709_551_615UL, "")]        // SetAffinity, full 64-bit mask
    [InlineData(2L, 5, 0, -1L, 4UL, "My Service")]                                          // ServiceControl with a name
    [InlineData(3L, 5, 0, 0L, 1UL, "name|with|pipes")]                                      // trailing text keeps pipes
    public void BrokerRequestRoundTripsThroughTheWire(
        long correlationId,
        int opValue,
        int processId,
        long creationTime,
        ulong argument,
        string text)
    {
        BrokerRequest original = new(correlationId, (ElevationOp)opValue, processId, creationTime, argument, text);

        Assert.True(BrokerRequest.TryParse(original.ToWire(), out BrokerRequest parsed));
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1|1|2|3")]                  // too few fields
    [InlineData("x|1|2|3|4|")]               // non-numeric correlation id
    [InlineData("1|1|2|3|notanumber|")]      // non-numeric argument
    [InlineData("1|999|2|3|4|")]             // undefined op value
    public void BrokerRequestRejectsMalformedLines(string? line)
    {
        Assert.False(BrokerRequest.TryParse(line, out _));
    }

    [Theory]
    [InlineData(7L, 0, "")]                                                 // Success
    [InlineData(8L, 1, "Access is denied.")]                               // AccessDenied
    [InlineData(9L, 4, "message with a | pipe and trailing text")]         // Failed
    public void BrokerResponseRoundTripsThroughTheWire(
        long correlationId,
        int codeValue,
        string message)
    {
        BrokerResponse original = new(correlationId, (BrokerResultCode)codeValue, message);

        Assert.True(BrokerResponse.TryParse(original.ToWire(), out BrokerResponse parsed));
        Assert.Equal(correlationId, parsed.CorrelationId);
        Assert.Equal((BrokerResultCode)codeValue, parsed.Code);
        Assert.Equal(message, parsed.Message);
    }

    [Fact]
    public void BrokerResponseCollapsesNewlinesInTheMessage()
    {
        BrokerResponse original = new(1L, BrokerResultCode.Failed, "line one\r\nline two");

        Assert.True(BrokerResponse.TryParse(original.ToWire(), out BrokerResponse parsed));
        Assert.DoesNotContain('\n', parsed.Message);
        Assert.DoesNotContain('\r', parsed.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1|0")]        // too few fields
    [InlineData("x|0|ok")]     // non-numeric correlation id
    [InlineData("1|999|ok")]   // undefined result code
    public void BrokerResponseRejectsMalformedLines(string? line)
    {
        Assert.False(BrokerResponse.TryParse(line, out _));
    }

    [Fact]
    public void CreatePipeNameIsUniqueAndPrefixed()
    {
        string first = ElevationBrokerContract.CreatePipeName();
        string second = ElevationBrokerContract.CreatePipeName();

        Assert.StartsWith("TaskManagerTrayAppDotNET-Broker-", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void IsBrokerLaunchDetectsTheModeArgumentOnly()
    {
        Assert.True(ElevationBroker.IsBrokerLaunch([ElevationBrokerContract.ModeArgument, "--broker-parent", "10"]));
        Assert.False(ElevationBroker.IsBrokerLaunch(["--autostart"]));
        Assert.False(ElevationBroker.IsBrokerLaunch([]));
    }

    [Fact]
    public void TrySetPriorityReportsAWin32CodeWhenTheTargetCannotBeOpened()
    {
        // A PID that will not resolve: expect failure with a non-zero Win32 code threaded out
        ProcessTerminationTarget target = new(ProcessID: 0x7FFFFFF0, CreationTimeFileTime: 0);

        bool succeeded = ProcessNativeActions.TrySetPriority(
            target,
            ProcessPriorityLevel.Normal,
            out string errorMessage,
            out int win32Error);

        Assert.False(succeeded);
        Assert.NotEqual(0, win32Error);
        Assert.False(string.IsNullOrEmpty(errorMessage));
    }

    [Theory]
    [InlineData(StartupAppApprovalTarget.RunSubKey)]
    [InlineData(StartupAppApprovalTarget.Run32SubKey)]
    [InlineData(StartupAppApprovalTarget.StartupFolderSubKey)]
    public void StartupApprovalWireAcceptsOnlyTheStartupApprovedSubKeys(string subKey)
    {
        StartupAppApprovalTarget target = new(
            StartupAppScope.AllUsers,
            StartupAppRegistryView.Registry64,
            subKey,
            ValueName: "Contoso");

        Assert.True(StartupApprovalWire.TryDecode(StartupApprovalWire.Encode(target), out StartupAppApprovalTarget decoded));
        Assert.Equal(target, decoded);
    }

    [Theory]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Run", "Contoso")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\Contoso", "ImagePath")]
    [InlineData(StartupAppApprovalTarget.RunSubKey + @"\..\..\Run", "Contoso")]
    [InlineData(StartupAppApprovalTarget.RunSubKey + @"\Nested", "Contoso")]
    [InlineData(StartupAppApprovalTarget.RunSubKey, "")]
    public void StartupApprovalWireRejectsOtherKeysAndEmptyValueNames(string subKey, string valueName)
    {
        StartupAppApprovalTarget target = new(
            StartupAppScope.AllUsers,
            StartupAppRegistryView.Registry64,
            subKey,
            valueName);

        Assert.False(target.IsValid);
        Assert.False(StartupApprovalWire.TryDecode(StartupApprovalWire.Encode(target), out _));
    }

    [Fact]
    public void MalformedRequestCorrelationIdIsRecoveredWhenPresent()
    {
        Assert.Equal(expected: 42L, BrokerRequest.ReadCorrelationId("42|999|1|2|3|"));
        Assert.Equal(expected: 0L, BrokerRequest.ReadCorrelationId("garbage"));
        Assert.Equal(expected: 0L, BrokerRequest.ReadCorrelationId(null));
    }

    [Fact]
    public void BrokerServesOnlyTheLaunchingParent()
    {
        Assert.True(ElevationBrokerContract.IsAuthorizedClient(clientProcessId: 1234, parentProcessId: 1234));
        Assert.False(ElevationBrokerContract.IsAuthorizedClient(clientProcessId: 4321, parentProcessId: 1234));
        Assert.False(ElevationBrokerContract.IsAuthorizedClient(clientProcessId: 0, parentProcessId: 0));

        Assert.True(ElevationBrokerContract.IsExpectedParent(expectedStartFileTime: 77, actualStartFileTime: 77));
        Assert.False(ElevationBrokerContract.IsExpectedParent(expectedStartFileTime: 77, actualStartFileTime: 78));
        Assert.False(ElevationBrokerContract.IsExpectedParent(expectedStartFileTime: 0, actualStartFileTime: 0));
    }

    [Fact]
    public void BrokerArgumentsRoundTripAndRequireTheParentStartTime()
    {
        string arguments = ElevationBrokerContract.BuildArguments(
            parentProcessId: 1234,
            parentStartFileTime: 638_000_000_000_000_000L,
            pipeName: "pipe-name");
        string[] parsedArguments = arguments.Split(' ');

        // The elevated broker must not take a user-controlled folder to write into
        Assert.DoesNotContain(TrayAppDotNETProgram.SettingsDirectoryArgument, arguments, StringComparison.Ordinal);

        Assert.True(ElevationBrokerContract.TryParseArguments(
            parsedArguments,
            out int parentProcessId,
            out long parentStartFileTime,
            out string pipeName));
        Assert.Equal(expected: 1234, parentProcessId);
        Assert.Equal(expected: 638_000_000_000_000_000L, parentStartFileTime);
        Assert.Equal(expected: "pipe-name", pipeName);

        Assert.False(ElevationBrokerContract.TryParseArguments(
            ["--elevated-broker", "--broker-parent", "1234", "--broker-pipe", "pipe-name"],
            out _,
            out _,
            out _));
    }

    [Fact]
    public void LongOperationsGetLongerResponseTimeouts()
    {
        Assert.True(ElevationBrokerContract.ResponseTimeout(ElevationOp.ServiceControl)
                    > ElevationBrokerContract.ResponseTimeout(ElevationOp.SetPriority));
        Assert.True(ElevationBrokerContract.ResponseTimeout(ElevationOp.CreateDump)
                    > ElevationBrokerContract.ResponseTimeout(ElevationOp.ServiceControl));
    }

    [Theory]
    [InlineData("ElevationBroker: serving the UI (PID 42).")]
    [InlineData("ElevationBroker.Execute(CreateDump): System.IO.IOException: boom\r\n   at A.B()\n   at C.D()")]
    [InlineData(@"path C:\temp\new\rate and a trailing backslash \")]
    [InlineData("")]
    public void BrokerLogLinesRoundTripOnOnePipeLine(string message)
    {
        string line = BrokerLogLine.Encode(message);

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.True(BrokerLogLine.TryDecode(line, out string decoded));
        Assert.Equal(message, decoded);
    }

    [Fact]
    public void BrokerLogLinesAreNeverMistakenForResponses()
    {
        string logLine = BrokerLogLine.Encode("12|0|looks like a response");
        string responseLine = new BrokerResponse(12, BrokerResultCode.Success, "log|not a log line").ToWire();

        Assert.False(BrokerResponse.TryParse(logLine, out _));
        Assert.False(BrokerLogLine.TryDecode(responseLine, out _));
        Assert.False(BrokerLogLine.TryDecode(null, out _));
        Assert.True(BrokerResponse.TryParse(responseLine, out BrokerResponse response));
        Assert.Equal(expected: "log|not a log line", response.Message);
    }

    [Fact]
    public void BrokerLogBufferKeepsTheNewestLinesAndReportsDroppedOnes()
    {
        BrokerLogBuffer buffer = new();
        for (int index = 0; index < BrokerLogBuffer.Capacity + 3; index++)
            buffer.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture));

        List<string> lines = buffer.TakeAll();

        Assert.Equal(BrokerLogBuffer.Capacity + 1, lines.Count);
        Assert.Contains("3 earlier log lines were dropped", lines[0], StringComparison.Ordinal);
        Assert.Equal(expected: "3", lines[1]);
        Assert.Equal(
            (BrokerLogBuffer.Capacity + 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
            lines[^1]);
        Assert.Empty(buffer.TakeAll());
    }

    [Fact]
    public void TimedOutRequestsSayTheActionMayStillComplete()
    {
        string message = ElevatedActionCoordinator.FormatTimedOutMessage(ElevationOp.CreateDump);

        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LocalizationManager.Instance[nameof(AppStrings.Elevation_TimedOutFormat)],
                ElevationBrokerContract.DumpOperationTimeout.TotalSeconds),
            message);
        Assert.NotEqual(
            LocalizationManager.Instance[nameof(AppStrings.Elevation_HelperStartFailed)],
            message);
    }

    [Fact]
    public void ATimedOutDumpNamesTheFileTheBrokerMayStillWrite()
    {
        const string dumpPath = @"C:\Users\someone\AppData\Local\Temp\TaskManagerTrayAppDotNET\app-42.dmp";

        string message = ElevatedActionCoordinator.FormatDumpTimedOutMessage(dumpPath);

        Assert.Contains(dumpPath, message, StringComparison.Ordinal);
        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LocalizationManager.Instance[nameof(AppStrings.Elevation_DumpTimedOutFormat)],
                ElevationBrokerContract.DumpOperationTimeout.TotalSeconds,
                dumpPath),
            message);
    }
}
