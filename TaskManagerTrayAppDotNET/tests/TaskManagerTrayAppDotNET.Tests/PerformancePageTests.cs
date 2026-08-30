using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformancePageTests
{
    [Fact]
    public void NetworkHoverMetricShowsSendThenReceive()
    {
        string metric = PerformancePage.FormatNetworkTransferHoverMetric(100, 200);

        Assert.Equal("Send: 100 B/s\nReceive: 200 B/s", metric);
    }

    [Fact]
    public void NetworkDeviceColumnHoverMetricUsesCompactLabels()
    {
        string metric = PerformancePage.FormatNetworkDeviceColumnHoverMetric(100, 200);

        Assert.Equal("S: 100 B/s\nR: 200 B/s", metric);
    }

    [Fact]
    public void MemoryDeviceColumnHoverMetricUsesGigabytesWithCompactSuffix()
    {
        const double Gibibyte = 1_073_741_824;

        string metric = PerformancePage.FormatMemoryDeviceColumnHoverMetric(4.5 * Gibibyte);

        Assert.Equal("4.5 G", metric);
    }
}
