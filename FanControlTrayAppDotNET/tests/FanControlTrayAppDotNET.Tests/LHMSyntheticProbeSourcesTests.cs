using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.Services;
using LibreHardwareMonitor.Hardware;
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

        Assert.Equal(expected: "AMD_Ryzen_9.Clock.Cores_(Max)", key);
    }

    /// <summary>
    /// Verifies synthetic keys follow the same space-normalized sensor key shape.
    /// </summary>
    [Fact]
    public void BuildMaxEffectiveClockKeyNormalizesHardwareName()
    {
        string key = LHMSyntheticProbeSources.BuildMaxEffectiveClockKey("AMD Ryzen 9");

        Assert.Equal(expected: "AMD_Ryzen_9.Clock.Cores_(Max_Effective)", key);
    }

    /// <summary>
    /// Verifies the total sums one whole-device reading per CPU and discrete GPU plus cooler pump/fan power.
    /// </summary>
    [Fact]
    public void PowerDrawTotalSumsComponentDevices()
    {
        PowerDrawTotalAccumulator total = new();

        AddDevice(total, HardwareType.Cpu, ("CPU Cores", 40.0f), ("CPU Package", 65.0f));
        AddDevice(total, HardwareType.GpuNvidia, ("GPU Package", 200.0f));
        AddDevice(total, HardwareType.Cooler, ("Pump Power", 5.0f), ("Fan Power", 2.0f), ("Dissipated Power", 300.0f));
        AddDevice(total, HardwareType.Motherboard, ("Package", 999.0f));

        Assert.Equal(expected: 272.0f, total.Total);
    }

    /// <summary>
    /// Verifies a device's best-ranked whole-device reading wins whatever order the sensors arrive in.
    /// </summary>
    [Fact]
    public void PowerDrawTotalUsesBestRankedDeviceReading()
    {
        PowerDrawTotalAccumulator total = new();

        AddDevice(total, HardwareType.Cpu, ("CPU Platform", 30.0f), ("CPU Package", 12.0f));
        AddDevice(total, HardwareType.GpuIntel, ("GPU Package", 90.0f), ("GPU Total", 120.0f));

        Assert.Equal(expected: 150.0f, total.Total);
    }

    /// <summary>
    /// Verifies Intel iGPU power is not added on top of the CPU package that already contains it.
    /// </summary>
    [Fact]
    public void PowerDrawTotalExcludesIntelIntegratedGpu()
    {
        bool integrated = LHMSyntheticProbeSources.IsIntegratedGpu(HardwareType.GpuIntel, "/gpu-intel-integrated/0");
        PowerDrawContribution contribution = LHMSyntheticProbeSources.ClassifyPowerDrawSensor(
            HardwareType.GpuIntel,
            integrated,
            SensorType.Power,
            "GPU Power");

        Assert.True(integrated);
        Assert.Equal(PowerDrawRole.None, contribution.Role);
        Assert.False(LHMSyntheticProbeSources.IsIntegratedGpu(HardwareType.GpuIntel, "/gpu-intel/0"));
    }

    /// <summary>
    /// Verifies a PSU total replaces the component sum, and several PSUs add up.
    /// </summary>
    [Fact]
    public void PowerDrawTotalPrefersPsuTotals()
    {
        PowerDrawTotalAccumulator total = new();

        AddDevice(total, HardwareType.Cpu, ("Package", 120.0f));
        AddDevice(total, HardwareType.Psu, ("Total Output", 300.0f), ("Total watts", 340.0f), ("+12V", 280.0f));
        AddDevice(total, HardwareType.Psu, ("PSU Out", 60.0f));

        Assert.Equal(expected: 400.0f, total.Total);
    }

    /// <summary>
    /// Verifies non-finite and negative readings are skipped and no contributor yields no total.
    /// </summary>
    [Fact]
    public void PowerDrawTotalSkipsInvalidReadings()
    {
        PowerDrawTotalAccumulator total = new();

        AddDevice(total, HardwareType.GpuAmd, ("GPU Package", float.NaN));
        AddDevice(total, HardwareType.Psu, ("Total watts", -1.0f));
        Assert.Null(total.Total);

        AddDevice(total, HardwareType.Cpu, ("Package", 50.0f));
        Assert.Equal(expected: 50.0f, total.Total);
    }

    private static void AddDevice(
        PowerDrawTotalAccumulator total,
        HardwareType hardwareType,
        params (string Name, float Value)[] sensors)
    {
        total.BeginDevice();
        foreach ((string name, float value) in sensors)
        {
            total.Add(
                LHMSyntheticProbeSources.ClassifyPowerDrawSensor(hardwareType, isIntegratedGpu: false, SensorType.Power, name),
                value);
        }

        total.EndDevice();
    }
}
