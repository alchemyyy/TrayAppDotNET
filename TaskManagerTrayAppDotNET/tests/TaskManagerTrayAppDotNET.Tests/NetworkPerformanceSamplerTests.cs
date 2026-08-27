using System.Diagnostics;
using System.Net.NetworkInformation;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class NetworkPerformanceSamplerTests
{
    [Fact]
    public void IncludesConnectedHardwareInterfaceWithAConnector()
    {
        Assert.True(NetworkPerformanceSampler.IsMeaningfulInterface(
            (uint)NetworkInterfaceType.Ethernet,
            true,
            false,
            true,
            false,
            false,
            true));
    }

    [Theory]
    [InlineData((uint)NetworkInterfaceType.Ethernet, false, false, true, false, false, true)]
    [InlineData((uint)NetworkInterfaceType.Ethernet, true, true, true, false, false, true)]
    [InlineData((uint)NetworkInterfaceType.Ethernet, true, false, false, false, false, true)]
    [InlineData((uint)NetworkInterfaceType.Ethernet, true, false, true, true, false, true)]
    [InlineData((uint)NetworkInterfaceType.Ethernet, true, false, true, false, true, true)]
    [InlineData((uint)NetworkInterfaceType.Ethernet, true, false, true, false, false, false)]
    [InlineData((uint)NetworkInterfaceType.Loopback, true, false, true, false, false, true)]
    [InlineData((uint)NetworkInterfaceType.Tunnel, true, false, true, false, false, true)]
    public void ExcludesVirtualFilterAndDisconnectedInterfaces(
        uint interfaceType,
        bool isHardwareInterface,
        bool isFilterInterface,
        bool isConnectorPresent,
        bool isMediaDisconnected,
        bool isEndPointInterface,
        bool isOperational)
    {
        Assert.False(NetworkPerformanceSampler.IsMeaningfulInterface(
            interfaceType,
            isHardwareInterface,
            isFilterInterface,
            isConnectorPresent,
            isMediaDisconnected,
            isEndPointInterface,
            isOperational));
    }

    [Fact]
    public void CalculatesRatesFromCumulativeCounters()
    {
        long elapsedTicks = checked(Stopwatch.Frequency * 2L);

        bool calculated = NetworkPerformanceSampler.TryCalculateThroughput(
            10_000,
            20_000,
            100,
            12_000,
            26_000,
            100 + elapsedTicks,
            out double receiveBytesPerSecond,
            out double sendBytesPerSecond);

        Assert.True(calculated);
        Assert.Equal(1_000, receiveBytesPerSecond);
        Assert.Equal(3_000, sendBytesPerSecond);
    }

    [Theory]
    [InlineData(100, 100, 90, 110, 1, 2)]
    [InlineData(100, 100, 110, 90, 1, 2)]
    [InlineData(100, 100, 110, 110, 2, 2)]
    public void RejectsCounterResetsAndInvalidIntervals(
        long previousReceived,
        long previousSent,
        long currentReceived,
        long currentSent,
        long previousTimestamp,
        long currentTimestamp)
    {
        bool calculated = NetworkPerformanceSampler.TryCalculateThroughput(
            previousReceived,
            previousSent,
            previousTimestamp,
            currentReceived,
            currentSent,
            currentTimestamp,
            out double receiveBytesPerSecond,
            out double sendBytesPerSecond);

        Assert.False(calculated);
        Assert.Equal(0, receiveBytesPerSecond);
        Assert.Equal(0, sendBytesPerSecond);
    }

    [Fact]
    public void NativeSamplesHaveStableUniqueConnectedInterfaceIDs()
    {
        NetworkPerformanceSampler sampler = new();
        long firstTimestamp = Stopwatch.GetTimestamp();

        NetworkPerformanceSnapshot[] first = sampler.Sample(firstTimestamp);
        NetworkPerformanceSnapshot[] second = sampler.Sample(firstTimestamp + Stopwatch.Frequency);

        Assert.Equal(
            first.Length,
            first.Select(static snapshot => snapshot.DeviceID).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            first.Select(static snapshot => snapshot.DeviceID),
            second.Select(static snapshot => snapshot.DeviceID));
        Assert.All(second, static snapshot =>
        {
            Assert.True(snapshot.IsOperational);
            Assert.True(snapshot.HasThroughputSample);
            Assert.StartsWith("network:", snapshot.DeviceID, StringComparison.Ordinal);
        });
    }
}
