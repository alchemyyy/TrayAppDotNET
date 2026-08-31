using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class WindowsServiceStateTests
{
    [Theory]
    [InlineData(0, 0, "Unknown")]
    [InlineData(1, 1, "Stopped")]
    [InlineData(2, 2, "Starting")]
    [InlineData(3, 3, "Stopping")]
    [InlineData(4, 4, "Running")]
    [InlineData(5, 5, "Continuing")]
    [InlineData(6, 6, "Pausing")]
    [InlineData(7, 7, "Paused")]
    [InlineData(99, 0, "Unknown")]
    public void NativeStatusMapsToStablePresentation(
        uint nativeStatus,
        int expectedStatusValue,
        string expectedText)
    {
        WindowsServiceStatus status = WindowsServiceState.FromNativeStatus(nativeStatus);
        WindowsServiceStatus expectedStatus = (WindowsServiceStatus)expectedStatusValue;

        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedText, WindowsServiceState.GetStatusText(status));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 5)]
    [InlineData(99, 0)]
    public void NativeStartTypeMapsToStableModel(
        uint nativeStartType,
        int expectedStartTypeValue)
    {
        WindowsServiceStartType expectedStartType = (WindowsServiceStartType)expectedStartTypeValue;
        Assert.Equal(expectedStartType, WindowsServiceState.FromNativeStartType(nativeStartType));
    }

    [Fact]
    public void ServiceTextAndPIDNormalizationHandlesMissingNativeValues()
    {
        Assert.Equal("Spooler", WindowsServiceState.NormalizeServiceName("  Spooler  "));
        Assert.Equal("Spooler", WindowsServiceState.NormalizeDisplayName("  ", "Spooler"));
        Assert.Equal("Print Spooler", WindowsServiceState.NormalizeDisplayName(" Print Spooler ", "Spooler"));
        Assert.Equal(string.Empty, WindowsServiceState.NormalizeOptionalText(null));
        Assert.Equal(0U, WindowsServiceState.NormalizePID(WindowsServiceStatus.Stopped, 4_242));
        Assert.Equal(4_242U, WindowsServiceState.NormalizePID(WindowsServiceStatus.Running, 4_242));
    }

    [Fact]
    public void StoppedManualServiceCanStartAndDisable()
    {
        WindowsServiceSnapshot service = CreateService(
            WindowsServiceStatus.Stopped,
            WindowsServiceStartType.OnDemand,
            WindowsServiceAcceptedControls.None);

        WindowsServiceActionState actions = WindowsServiceState.GetActionState(service);

        Assert.True(actions.CanStart);
        Assert.False(actions.CanStop);
        Assert.False(actions.CanRestart);
        Assert.True(actions.CanDisable);
    }

    [Fact]
    public void RunningStoppableServiceCanStopRestartAndDisable()
    {
        WindowsServiceSnapshot service = CreateService(
            WindowsServiceStatus.Running,
            WindowsServiceStartType.Automatic,
            WindowsServiceAcceptedControls.Stop | WindowsServiceAcceptedControls.Shutdown);

        WindowsServiceActionState actions = WindowsServiceState.GetActionState(service);

        Assert.False(actions.CanStart);
        Assert.True(actions.CanStop);
        Assert.True(actions.CanRestart);
        Assert.True(actions.CanDisable);
    }

    [Fact]
    public void DisabledRunningServiceCanStopButCannotRestartOrDisableAgain()
    {
        WindowsServiceSnapshot service = CreateService(
            WindowsServiceStatus.Running,
            WindowsServiceStartType.Disabled,
            WindowsServiceAcceptedControls.Stop);

        WindowsServiceActionState actions = WindowsServiceState.GetActionState(service);

        Assert.False(actions.CanStart);
        Assert.True(actions.CanStop);
        Assert.False(actions.CanRestart);
        Assert.False(actions.CanDisable);
    }

    [Fact]
    public void UnknownServiceStateDisablesAllMutatingButtons()
    {
        WindowsServiceSnapshot service = CreateService(
            WindowsServiceStatus.Unknown,
            WindowsServiceStartType.Automatic,
            WindowsServiceAcceptedControls.Stop);

        WindowsServiceActionState actions = WindowsServiceState.GetActionState(service);

        Assert.False(actions.CanStart);
        Assert.False(actions.CanStop);
        Assert.False(actions.CanRestart);
        Assert.False(actions.CanDisable);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void PendingServiceDisablesAllMutatingButtons(int statusValue)
    {
        WindowsServiceStatus status = (WindowsServiceStatus)statusValue;
        WindowsServiceSnapshot service = CreateService(
            status,
            WindowsServiceStartType.Automatic,
            WindowsServiceAcceptedControls.Stop);

        WindowsServiceActionState actions = WindowsServiceState.GetActionState(service);

        Assert.False(actions.CanStart);
        Assert.False(actions.CanStop);
        Assert.False(actions.CanRestart);
        Assert.False(actions.CanDisable);
    }

    private static WindowsServiceSnapshot CreateService(
        WindowsServiceStatus status,
        WindowsServiceStartType startType,
        WindowsServiceAcceptedControls acceptedControls) =>
        new(
            "ExampleService",
            "Example Service",
            status == WindowsServiceStatus.Stopped ? 0U : 123U,
            "Example service used by a pure unit test",
            status,
            string.Empty,
            startType,
            acceptedControls);
}
