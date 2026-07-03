using System.Globalization;
using NCalc;

namespace FanControlTrayAppDotNET.UI;

/// <summary>
/// Formats probe values for selector and flyout display.
/// </summary>
internal static class ProbeValueFormatter
{
    private const string DecimalProbeValueFormat = "0.00";
    private const string TruncatedProbeValueFormat = "0";

    /// <summary>
    /// Returns true when the data source belongs in the probe selector tabs.
    /// </summary>
    public static bool IsProbeDataSource(DataSource source) =>
        source.DataSourceType is DataSourceTypeEnum.Temperature
            or DataSourceTypeEnum.Power
            or DataSourceTypeEnum.Load
            or DataSourceTypeEnum.Clock
            or DataSourceTypeEnum.Voltage;

    /// <summary>
    /// Resolves the glyph used for a probe type.
    /// </summary>
    public static string GlyphFor(DataSourceTypeEnum type) => type switch
    {
        DataSourceTypeEnum.Temperature => GlyphCatalog.TEMPERATURE,
        DataSourceTypeEnum.Power => GlyphCatalog.WATTAGE,
        DataSourceTypeEnum.Load => GlyphCatalog.LOAD,
        DataSourceTypeEnum.Clock => GlyphCatalog.CLOCK,
        DataSourceTypeEnum.Voltage => GlyphCatalog.VOLTAGE,
        _ => GlyphCatalog.PROBE,
    };

    /// <summary>
    /// Formats a probe value after applying its optional NCalc transform.
    /// </summary>
    public static string FormatValue(DataSource source, ProbeCardProbe? probe, bool truncate = false)
    {
        double value = TransformValue(source.DisplayValue, probe?.TransformString);
        if (truncate)
            value = Math.Round(value, MidpointRounding.AwayFromZero);

        string formatted = value.ToString(
            truncate ? TruncatedProbeValueFormat : DecimalProbeValueFormat,
            CultureInfo.InvariantCulture);
        string unit = source.DisplayUnit;
        return string.IsNullOrWhiteSpace(unit) ? formatted : $"{formatted} {unit}";
    }

    /// <summary>
    /// Applies an NCalc transform where x or X is the display value.
    /// </summary>
    public static double TransformValue(double value, string? transformString)
    {
        if (string.IsNullOrWhiteSpace(transformString)) return value;

        try
        {
            Expression expression = new(transformString)
            {
                Parameters =
                {
                    ["x"] = value,
                    ["X"] = value,
                },
            };
            object? result = expression.Evaluate();
            if (result is IConvertible convertible)
                return Convert.ToDouble(convertible, CultureInfo.InvariantCulture);
        }
        catch
        {
            return value;
        }

        return value;
    }
}
