using System.Diagnostics;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessorFrequencyAccumulatorTests
{
    // Matches the 625 kHz base measured on Windows 11, so one second is 625,000 counter units
    private const uint CounterUnitsPerSecond = 625_000;

    [Fact]
    public void NearIdleWindowIsHeldUntilEnoughBusyTimeAccumulates()
    {
        ProcessorFrequencyAccumulator accumulator = new();
        _ = accumulator.Update(Reading(weightedBusyTime: 0, busyTime: 0), elapsedTime: 0, Timestamp(seconds: 0));

        // A few microseconds of busy time with a nonsense ratio must not be reported on its own
        uint idleBusyUnits = CounterUnits(seconds: 0.00008);
        long weightedBusyTime = idleBusyUnits * 40_000L;
        uint busyTime = idleBusyUnits;
        bool hasSpikeReading = accumulator.Update(
            Reading(weightedBusyTime, busyTime),
            CounterUnits(seconds: 0.1),
            Timestamp(seconds: 0.1));

        uint busyBusyUnits = CounterUnits(seconds: 0.050);
        weightedBusyTime += busyBusyUnits * 5_000L;
        busyTime += busyBusyUnits;
        bool hasReading = accumulator.Update(
            Reading(weightedBusyTime, busyTime),
            CounterUnits(seconds: 0.2),
            Timestamp(seconds: 0.2));

        Assert.False(hasSpikeReading);
        Assert.True(hasReading);
        Assert.Equal(weightedBusyTime / (double)busyTime, accumulator.Megahertz, precision: 6);
    }

    [Fact]
    public void LightLoadQualifiesAfterAccumulatingAcrossCollections()
    {
        ProcessorFrequencyAccumulator accumulator = new();
        _ = accumulator.Update(Reading(weightedBusyTime: 0, busyTime: 0), elapsedTime: 0, Timestamp(seconds: 0));

        uint busyUnitsPerCollection = CounterUnits(seconds: 0.006);
        long weightedBusyTime = 0;
        uint busyTime = 0;
        for (int collectionIndex = 1; collectionIndex <= 3; collectionIndex++)
        {
            weightedBusyTime += busyUnitsPerCollection * 4_800L;
            busyTime += busyUnitsPerCollection;
            bool hasEarlyReading = accumulator.Update(
                Reading(weightedBusyTime, busyTime),
                CounterUnits(seconds: 0.1 * collectionIndex),
                Timestamp(seconds: 0.1 * collectionIndex));
            Assert.False(hasEarlyReading);
        }

        weightedBusyTime += busyUnitsPerCollection * 4_800L;
        busyTime += busyUnitsPerCollection;
        bool hasReading = accumulator.Update(
            Reading(weightedBusyTime, busyTime),
            CounterUnits(seconds: 0.4),
            Timestamp(seconds: 0.4));

        Assert.True(hasReading);
        Assert.Equal(expected: 4_800.0, accumulator.Megahertz, precision: 6);
    }

    [Fact]
    public void WrappingBaseCountersProduceTheRealRatio()
    {
        ProcessorFrequencyAccumulator accumulator = new();
        const uint busyBaseline = uint.MaxValue - 1_000;
        const uint elapsedBaseline = uint.MaxValue - 100;
        _ = accumulator.Update(
            Reading(weightedBusyTime: 5_000_000, busyBaseline),
            elapsedBaseline,
            Timestamp(seconds: 0));

        uint busyDelta = CounterUnits(seconds: 0.050);
        bool hasReading = accumulator.Update(
            Reading(5_000_000 + busyDelta * 5_250L, unchecked(busyBaseline + busyDelta)),
            unchecked(elapsedBaseline + CounterUnits(seconds: 0.1)),
            Timestamp(seconds: 0.1));

        Assert.True(hasReading);
        Assert.Equal(expected: 5_250.0, accumulator.Megahertz, precision: 6);
    }

    [Fact]
    public void QualifiedReadingIsHeldThroughCollectionsWithoutEnoughBusyTime()
    {
        ProcessorFrequencyAccumulator accumulator = new();
        _ = accumulator.Update(Reading(weightedBusyTime: 0, busyTime: 0), elapsedTime: 0, Timestamp(seconds: 0));
        uint busyUnits = CounterUnits(seconds: 0.030);
        _ = accumulator.Update(
            Reading(busyUnits * 5_400L, busyUnits),
            CounterUnits(seconds: 0.1),
            Timestamp(seconds: 0.1));

        uint idleUnits = CounterUnits(seconds: 0.0001);
        bool hasReading = accumulator.Update(
            Reading(busyUnits * 5_400L + idleUnits * 40_000L, busyUnits + idleUnits),
            CounterUnits(seconds: 0.2),
            Timestamp(seconds: 0.2));

        Assert.False(hasReading);
        Assert.Equal(expected: 5_400.0, accumulator.Megahertz, precision: 6);
    }

    [Fact]
    public void CounterResetRestartsAccumulationWithoutAReading()
    {
        ProcessorFrequencyAccumulator accumulator = new();
        _ = accumulator.Update(
            Reading(weightedBusyTime: 900_000_000, busyTime: 700_000),
            elapsedTime: 0,
            Timestamp(seconds: 0));

        uint busyUnits = CounterUnits(seconds: 0.050);
        bool hasResetReading = accumulator.Update(
            Reading(busyUnits * 4_600L, busyUnits),
            CounterUnits(seconds: 0.1),
            Timestamp(seconds: 0.1));
        bool hasReading = accumulator.Update(
            Reading(busyUnits * 4_600L * 2, busyUnits * 2),
            CounterUnits(seconds: 0.2),
            Timestamp(seconds: 0.2));

        Assert.False(hasResetReading);
        Assert.True(hasReading);
        Assert.Equal(expected: 4_600.0, accumulator.Megahertz, precision: 6);
    }

    [Fact]
    public void ResetDiscardsBaselinesAndTheHeldReading()
    {
        ProcessorFrequencyAccumulator accumulator = new();
        _ = accumulator.Update(Reading(weightedBusyTime: 0, busyTime: 0), elapsedTime: 0, Timestamp(seconds: 0));
        uint busyUnits = CounterUnits(seconds: 0.050);
        _ = accumulator.Update(
            Reading(busyUnits * 5_400L, busyUnits),
            CounterUnits(seconds: 0.1),
            Timestamp(seconds: 0.1));

        accumulator.Reset();
        bool hasReading = accumulator.Update(
            Reading(busyUnits * 10_800L, busyUnits * 2),
            CounterUnits(seconds: 0.2),
            Timestamp(seconds: 0.2));

        Assert.False(hasReading);
        Assert.Equal(expected: 0.0, accumulator.Megahertz);
    }

    private static ProcessorFrequencyReading Reading(long weightedBusyTime, uint busyTime) =>
        new(weightedBusyTime, busyTime);

    private static uint CounterUnits(double seconds) =>
        (uint)Math.Round(seconds * CounterUnitsPerSecond, MidpointRounding.AwayFromZero);

    private static long Timestamp(double seconds) =>
        (long)Math.Round(seconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero);
}
