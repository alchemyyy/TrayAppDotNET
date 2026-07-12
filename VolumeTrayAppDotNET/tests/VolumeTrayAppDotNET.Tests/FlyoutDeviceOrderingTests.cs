using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class FlyoutDeviceOrderingTests
{
    [Fact]
    public void OrderStateGroupedSeparatesCaptureFromRenderAcrossStateBuckets()
    {
        TestDevice[] devices =
        [
            new TestDevice("Default microphone", EDataFlow.eCapture, 0),
            new TestDevice("Digital output playback", EDataFlow.eRender, 3),
            new TestDevice("Bluetooth headset microphone", EDataFlow.eCapture, 2),
            new TestDevice("Disconnected microphone", EDataFlow.eCapture, 4),
            new TestDevice("Bluetooth headset playback", EDataFlow.eRender, 0)
        ];

        List<TestDevice> ordered = FlyoutDeviceOrdering.OrderStateGrouped(
            devices,
            intermix: false,
            static device => device.DataFlow,
            static device => device.StateBucket);

        Assert.Equal(
            [
                "Disconnected microphone",
                "Bluetooth headset microphone",
                "Default microphone",
                "Digital output playback",
                "Bluetooth headset playback"
            ],
            ordered.Select(static device => device.Name));
    }

    [Fact]
    public void OrderStateGroupedPreservesStateFirstOrderingWhenIntermixed()
    {
        TestDevice[] devices =
        [
            new TestDevice("Default microphone", EDataFlow.eCapture, 0),
            new TestDevice("Digital output playback", EDataFlow.eRender, 3),
            new TestDevice("Bluetooth headset microphone", EDataFlow.eCapture, 2),
            new TestDevice("Disconnected microphone", EDataFlow.eCapture, 4),
            new TestDevice("Bluetooth headset playback", EDataFlow.eRender, 0)
        ];

        List<TestDevice> ordered = FlyoutDeviceOrdering.OrderStateGrouped(
            devices,
            intermix: true,
            static device => device.DataFlow,
            static device => device.StateBucket);

        Assert.Equal(
            [
                "Disconnected microphone",
                "Digital output playback",
                "Bluetooth headset microphone",
                "Bluetooth headset playback",
                "Default microphone"
            ],
            ordered.Select(static device => device.Name));
    }

    private readonly record struct TestDevice(string Name, EDataFlow DataFlow, int StateBucket);
}
