using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

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
}
