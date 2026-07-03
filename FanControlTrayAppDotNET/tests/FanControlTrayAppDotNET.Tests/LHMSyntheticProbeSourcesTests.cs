using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.Services;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class LHMSyntheticProbeSourcesTests
{
    /// <summary>
    /// Verifies per-core non-effective clock sensors are accepted.
    /// </summary>
    [Theory]
    [InlineData("Core #1")]
    [InlineData("P-Core #2")]
    [InlineData("E-Core #10")]
    [InlineData("CPU Core")]
    public void IsCoreClockSensorAcceptsPerCoreClocks(string sensorName)
    {
        bool result = LHMSyntheticProbeSources.IsCoreClockSensor(DataSourceTypeEnum.Clock, sensorName);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies effective, average, and non-clock sensors are excluded from max-clock input.
    /// </summary>
    [Theory]
    [InlineData(DataSourceTypeEnum.Clock, "Core #1 (Effective)")]
    [InlineData(DataSourceTypeEnum.Clock, "Cores (Average)")]
    [InlineData(DataSourceTypeEnum.Temperature, "Core #1")]
    public void IsCoreClockSensorRejectsEffectiveAggregateOrWrongTypeSensors(
        DataSourceTypeEnum sourceType,
        string sensorName)
    {
        bool result = LHMSyntheticProbeSources.IsCoreClockSensor(sourceType, sensorName);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies per-core effective clock sensors are accepted.
    /// </summary>
    [Theory]
    [InlineData("Core #1 (Effective)")]
    [InlineData("P-Core #2 (Effective)")]
    [InlineData("E-Core #10 (Effective)")]
    public void IsCoreEffectiveClockSensorAcceptsPerCoreEffectiveClocks(string sensorName)
    {
        bool result = LHMSyntheticProbeSources.IsCoreEffectiveClockSensor(DataSourceTypeEnum.Clock, sensorName);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies average and non-clock sensors are excluded from max-effective-clock input.
    /// </summary>
    [Theory]
    [InlineData(DataSourceTypeEnum.Clock, "Cores (Average Effective)")]
    [InlineData(DataSourceTypeEnum.Clock, "CPU Core")]
    [InlineData(DataSourceTypeEnum.Temperature, "Core #1 (Effective)")]
    public void IsCoreEffectiveClockSensorRejectsAggregateOrWrongTypeSensors(
        DataSourceTypeEnum sourceType,
        string sensorName)
    {
        bool result = LHMSyntheticProbeSources.IsCoreEffectiveClockSensor(sourceType, sensorName);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies max-clock synthetic keys follow the same space-normalized sensor key shape.
    /// </summary>
    [Fact]
    public void BuildMaxClockKeyNormalizesHardwareName()
    {
        string key = LHMSyntheticProbeSources.BuildMaxClockKey("AMD Ryzen 9");

        Assert.Equal("AMD_Ryzen_9.Clock.Cores_(Max)", key);
    }

    /// <summary>
    /// Verifies synthetic keys follow the same space-normalized sensor key shape.
    /// </summary>
    [Fact]
    public void BuildMaxEffectiveClockKeyNormalizesHardwareName()
    {
        string key = LHMSyntheticProbeSources.BuildMaxEffectiveClockKey("AMD Ryzen 9");

        Assert.Equal("AMD_Ryzen_9.Clock.Cores_(Max_Effective)", key);
    }
}
