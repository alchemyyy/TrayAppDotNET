using System.Diagnostics;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessorSpeedEstimatorTests
{
    // Matches the 625 kHz base measured on Windows 11, so one second is 625,000 counter units
    private const uint CounterUnitsPerSecond = 625_000;
    private const int FirstProcessor = 0;
    private const int SecondProcessor = 1;

    [Fact]
    public void NearIdleProcessorSpikeIsIgnoredWhileBusyProcessorQualifies()
    {
        ProcessorSpeedEstimator estimator = new();
        _ = estimator.Update(
            [
                Reading(FirstProcessor, weightedBusyTime: 0, busyTime: 0),
                Reading(SecondProcessor, weightedBusyTime: 0, busyTime: 0)
            ],
            elapsedTime: 0,
            Timestamp(seconds: 0));

        uint idleBusyUnits = CounterUnits(seconds: 0.00008);
        uint busyBusyUnits = CounterUnits(seconds: 0.050);
        bool hasEstimate = estimator.Update(
            [
                Reading(FirstProcessor, idleBusyUnits * 800L, idleBusyUnits),
                Reading(SecondProcessor, busyBusyUnits * 130L, busyBusyUnits)
            ],
            CounterUnits(seconds: 0.1),
            Timestamp(seconds: 0.1));

        Assert.True(hasEstimate);
        Assert.Equal(expected: 130.0, estimator.HighestPerformancePercent, precision: 6);
    }

    [Fact]
    public void LightlyBusyProcessorQualifiesAfterAccumulatingAcrossCollections()
    {
        ProcessorSpeedEstimator estimator = new();
        _ = estimator.Update(
            [Reading(FirstProcessor, weightedBusyTime: 0, busyTime: 0)],
            elapsedTime: 0,
            Timestamp(seconds: 0));

        uint busyUnitsPerCollection = CounterUnits(seconds: 0.006);
        long weightedBusyTime = 0;
        uint busyTime = 0;
        for (int collectionIndex = 1; collectionIndex <= 3; collectionIndex++)
        {
            weightedBusyTime += busyUnitsPerCollection * 120L;
            busyTime += busyUnitsPerCollection;
            bool hasEarlyEstimate = estimator.Update(
                [Reading(FirstProcessor, weightedBusyTime, busyTime)],
                CounterUnits(seconds: 0.1 * collectionIndex),
                Timestamp(seconds: 0.1 * collectionIndex));
            Assert.False(hasEarlyEstimate);
        }

        weightedBusyTime += busyUnitsPerCollection * 120L;
        busyTime += busyUnitsPerCollection;
        bool hasEstimate = estimator.Update(
            [Reading(FirstProcessor, weightedBusyTime, busyTime)],
            CounterUnits(seconds: 0.4),
            Timestamp(seconds: 0.4));

        Assert.True(hasEstimate);
        Assert.Equal(expected: 120.0, estimator.HighestPerformancePercent, precision: 6);
    }

    [Fact]
    public void WrappingBaseCountersProduceTheRealRatio()
    {
        ProcessorSpeedEstimator estimator = new();
        const uint busyBaseline = uint.MaxValue - 1_000;
        const uint elapsedBaseline = uint.MaxValue - 100;
        _ = estimator.Update(
            [Reading(FirstProcessor, weightedBusyTime: 5_000_000, busyBaseline)],
            elapsedBaseline,
            Timestamp(seconds: 0));

        uint busyDelta = CounterUnits(seconds: 0.050);
        bool hasEstimate = estimator.Update(
            [Reading(FirstProcessor, 5_000_000 + busyDelta * 125L, unchecked(busyBaseline + busyDelta))],
            unchecked(elapsedBaseline + CounterUnits(seconds: 0.1)),
            Timestamp(seconds: 0.1));

        Assert.True(hasEstimate);
        Assert.Equal(expected: 125.0, estimator.HighestPerformancePercent, precision: 6);
    }

    [Fact]
    public void QualifiedEstimateIsHeldThroughCollectionsWithoutEnoughBusyTime()
    {
        ProcessorSpeedEstimator estimator = new();
        _ = estimator.Update(
            [Reading(FirstProcessor, weightedBusyTime: 0, busyTime: 0)],
            elapsedTime: 0,
            Timestamp(seconds: 0));
        uint busyUnits = CounterUnits(seconds: 0.030);
        _ = estimator.Update(
            [Reading(FirstProcessor, busyUnits * 130L, busyUnits)],
            CounterUnits(seconds: 0.1),
            Timestamp(seconds: 0.1));

        uint idleUnits = CounterUnits(seconds: 0.0001);
        bool hasEstimate = estimator.Update(
            [Reading(FirstProcessor, busyUnits * 130L + idleUnits * 900L, busyUnits + idleUnits)],
            CounterUnits(seconds: 0.2),
            Timestamp(seconds: 0.2));

        Assert.False(hasEstimate);
        Assert.Equal(expected: 130.0, estimator.HighestPerformancePercent, precision: 6);
    }

    [Fact]
    public void CounterResetRestartsAccumulationWithoutAnEstimate()
    {
        ProcessorSpeedEstimator estimator = new();
        _ = estimator.Update(
            [Reading(FirstProcessor, weightedBusyTime: 90_000_000, busyTime: 700_000)],
            elapsedTime: 0,
            Timestamp(seconds: 0));

        uint busyUnits = CounterUnits(seconds: 0.050);
        bool hasResetEstimate = estimator.Update(
            [Reading(FirstProcessor, busyUnits * 110L, busyUnits)],
            CounterUnits(seconds: 0.1),
            Timestamp(seconds: 0.1));
        bool hasEstimate = estimator.Update(
            [Reading(FirstProcessor, busyUnits * 110L * 2, busyUnits * 2)],
            CounterUnits(seconds: 0.2),
            Timestamp(seconds: 0.2));

        Assert.False(hasResetEstimate);
        Assert.True(hasEstimate);
        Assert.Equal(expected: 110.0, estimator.HighestPerformancePercent, precision: 6);
    }

    [Fact]
    public void ResetDiscardsBaselinesAndTheHeldEstimate()
    {
        ProcessorSpeedEstimator estimator = new();
        _ = estimator.Update(
            [Reading(FirstProcessor, weightedBusyTime: 0, busyTime: 0)],
            elapsedTime: 0,
            Timestamp(seconds: 0));
        uint busyUnits = CounterUnits(seconds: 0.050);
        _ = estimator.Update(
            [Reading(FirstProcessor, busyUnits * 130L, busyUnits)],
            CounterUnits(seconds: 0.1),
            Timestamp(seconds: 0.1));

        estimator.Reset();
        bool hasEstimate = estimator.Update(
            [Reading(FirstProcessor, busyUnits * 260L, busyUnits * 2)],
            CounterUnits(seconds: 0.2),
            Timestamp(seconds: 0.2));

        Assert.False(hasEstimate);
        Assert.Equal(expected: 0.0, estimator.HighestPerformancePercent);
    }

    [Theory]
    [InlineData("0,5", 5)]
    [InlineData("1,3", (1 << 16) | 3)]
    [InlineData("7", 7)]
    public void LogicalProcessorInstanceNamesMapToStableKeys(string instanceName, int expectedKey)
    {
        Assert.True(SystemPerformanceMetadataReader.TryParseLogicalProcessorKey(
            instanceName,
            out int processorKey));
        Assert.Equal(expectedKey, processorKey);
    }

    [Theory]
    [InlineData("_Total")]
    [InlineData("0,_Total")]
    [InlineData("")]
    [InlineData(",3")]
    [InlineData("3,")]
    [InlineData("1,2,3")]
    public void TotalAndMalformedInstanceNamesAreRejected(string instanceName) =>
        Assert.False(SystemPerformanceMetadataReader.TryParseLogicalProcessorKey(instanceName, out _));

    private static ProcessorPerformanceReading Reading(int processorKey, long weightedBusyTime, uint busyTime) =>
        new(processorKey, weightedBusyTime, busyTime);

    private static uint CounterUnits(double seconds) =>
        (uint)Math.Round(seconds * CounterUnitsPerSecond, MidpointRounding.AwayFromZero);

    private static long Timestamp(double seconds) =>
        (long)Math.Round(seconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero);
}
