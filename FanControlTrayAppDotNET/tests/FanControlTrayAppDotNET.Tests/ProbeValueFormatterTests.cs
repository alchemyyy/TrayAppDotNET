using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class ProbeValueFormatterTests
{
    /// <summary>
    /// Verifies probe values keep fixed decimal precision.
    /// </summary>
    [Theory]
    [InlineData(DataSourceTypeEnum.Temperature, 42000, "42.00 C")]
    [InlineData(DataSourceTypeEnum.Power, 125500, "125.50 W")]
    [InlineData(DataSourceTypeEnum.Load, 100000, "100.00 %")]
    [InlineData(DataSourceTypeEnum.Voltage, 1250, "1.25 V")]
    [InlineData(DataSourceTypeEnum.Clock, 5000000, "5000.00 MHz")]
    public void FormatValuePadsDecimalProbeValues(DataSourceTypeEnum type, long value, string expected)
    {
        DataSource source = new()
        {
            DataSourceType = type,
            Value = value,
        };

        string formatted = ProbeValueFormatter.FormatValue(source, null);

        Assert.Equal(expected, formatted);
    }

    /// <summary>
    /// Verifies transformed values keep fixed decimal precision.
    /// </summary>
    [Fact]
    public void FormatValuePadsTransformedProbeValues()
    {
        DataSource source = new()
        {
            DataSourceType = DataSourceTypeEnum.Temperature,
            Value = 40000,
        };
        ProbeCardProbe probe = new()
        {
            TransformString = "x + 2",
        };

        string formatted = ProbeValueFormatter.FormatValue(source, probe);

        Assert.Equal("42.00 C", formatted);
    }

    /// <summary>
    /// Verifies truncated probe values round to whole numbers.
    /// </summary>
    [Theory]
    [InlineData(42490, "42 C")]
    [InlineData(42500, "43 C")]
    public void FormatValueRoundsTruncatedProbeValues(long value, string expected)
    {
        DataSource source = new()
        {
            DataSourceType = DataSourceTypeEnum.Temperature,
            Value = value,
        };

        string formatted = ProbeValueFormatter.FormatValue(source, null, truncate: true);

        Assert.Equal(expected, formatted);
    }
}
