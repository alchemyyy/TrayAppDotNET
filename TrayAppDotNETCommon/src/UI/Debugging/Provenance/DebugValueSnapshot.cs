#if DEBUG
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Creates bounded diagnostic text without retaining assigned property values.</summary>
internal readonly record struct DebugValueSnapshot(string TypeName, string Display)
{
    private const int MaximumDisplayLength = 240;

    public static DebugValueSnapshot Create(object? value)
    {
        if (value == null) return new DebugValueSnapshot("<null>", "<null>");

        Type valueType = value.GetType();
        string typeName = valueType.FullName ?? valueType.Name;
        string display = value switch
        {
            string text => text,
            char character => character.ToString(),
            bool boolean => boolean ? "true" : "false",
            byte number => number.ToString(CultureInfo.InvariantCulture),
            sbyte number => number.ToString(CultureInfo.InvariantCulture),
            short number => number.ToString(CultureInfo.InvariantCulture),
            ushort number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            ulong number => number.ToString(CultureInfo.InvariantCulture),
            float number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            DateTime timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture),
            Guid identifier => identifier.ToString("D"),
            Color color => color.ToString(),
            Thickness thickness => thickness.ToString(),
            CornerRadius cornerRadius => cornerRadius.ToString(),
            Rect rectangle => rectangle.ToString(),
            Size size => size.ToString(),
            Point point => point.ToString(),
            Vector vector => vector.ToString(),
            Matrix matrix => matrix.ToString(),
            GridLength gridLength => gridLength.ToString(),
            FontWeight fontWeight => fontWeight.ToString(),
            FontStretch fontStretch => fontStretch.ToString(),
            ISolidColorBrush brush =>
                $"{brush.GetType().Name} Color={brush.Color}, Opacity={FormatNumber(brush.Opacity)}",
            GradientBrush brush => $"{brush.GetType().Name} Stops={brush.GradientStops.Count}, Opacity={FormatNumber(brush.Opacity)}",
            FontFamily fontFamily => fontFamily.Name,
            Enum enumValue => enumValue.ToString(),
            Control control => string.IsNullOrWhiteSpace(control.Name)
                ? control.GetType().Name
                : $"{control.GetType().Name}#{control.Name}",
            _ => typeName
        };

        display = display.Replace('\r', ' ').Replace('\n', ' ');
        if (display.Length > MaximumDisplayLength)
            display = display[..(MaximumDisplayLength - 3)] + "...";

        return new DebugValueSnapshot(typeName, display);
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
#endif
