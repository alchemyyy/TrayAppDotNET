using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class StartupApprovalWireTests
{
    [Theory]
    [InlineData(0, 2, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "My App")]
    [InlineData(1, 1, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", "name with spaces")]
    [InlineData(1, 2, @"Software\...\StartupApproved\StartupFolder", "value|with|pipes")]
    public void RoundTripsThroughTheWire(int scope, int view, string subKey, string valueName)
    {
        StartupAppApprovalTarget original = new(
            (StartupAppScope)scope,
            (StartupAppRegistryView)view,
            subKey,
            valueName);

        Assert.True(StartupApprovalWire.TryDecode(StartupApprovalWire.Encode(original), out StartupAppApprovalTarget decoded));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void RoundTripsThroughBothTheStartupAndBrokerWire()
    {
        // A value name containing '|' must survive the broker request wire (where text is the trailing field)
        StartupAppApprovalTarget original = new(
            StartupAppScope.AllUsers,
            StartupAppRegistryView.Registry64,
            @"Software\X\StartupApproved\Run",
            "odd|name");
        BrokerRequest request = new(1, ElevationOp.SetStartupApproval, 0, 0, 1UL, StartupApprovalWire.Encode(original));

        Assert.True(BrokerRequest.TryParse(request.ToWire(), out BrokerRequest parsed));
        Assert.True(StartupApprovalWire.TryDecode(parsed.Text, out StartupAppApprovalTarget decoded));
        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("only\u001Ftwo")]                         // wrong field count
    [InlineData("x\u001F1\u001Fsub\u001Fval")]            // non-numeric scope
    [InlineData("9\u001F1\u001Fsub\u001Fval")]            // undefined scope value
    [InlineData("0\u001F9\u001Fsub\u001Fval")]            // undefined registry view value
    public void RejectsMalformedText(string? text)
    {
        Assert.False(StartupApprovalWire.TryDecode(text, out _));
    }
}
