using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.Localization;

namespace BrightnessTrayAppDotNET.UI.Settings.Environmental;

public sealed partial class EnvironmentalCurveEditor
{
    private double TopInset => PlotInsetYBase + (_disabledPeriodEnabled ? DisabledPeriodPinAreaHeight : 0.0);

    private Rect PlotRect()
    {
        double left = AxisGutterWidth + PlotInsetX;
        double right = Math.Max(left, Bounds.Width - AxisGutterWidth - PlotInsetX);
        double top = TopInset;
        double bottom = Math.Max(top, Bounds.Height - TimeAxisHeight - PlotInsetYBase);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static double ScreenX(double t, Rect plot) => plot.Left + t * plot.Width;

    private static double FromScreenX(double x, Rect plot) => plot.Width <= 0 ? 0 : (x - plot.Left) / plot.Width;

    private static double ScreenY(double v, Rect plot) => plot.Top + (1.0 - v / 100.0) * plot.Height;

    private static double FromScreenY(double y, Rect plot) =>
        plot.Height <= 0 ? 0 : (1.0 - (y - plot.Top) / plot.Height) * 100.0;

    private void ApplyCurveSelection()
    {
        if (_curveData == null) return;
        _brightness = _offsetMode ? _curveData.BrightnessOffset : _curveData.Brightness;
        _nightLight = _offsetMode ? _curveData.NightLightOffset : _curveData.NightLight;
    }

    private void StartCurrentTimeTimer()
    {
        if (_currentTimeTimer != null) return;
        _currentTimeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.CurveEditorClockIndicatorRefreshIntervalMs),
        };
        _currentTimeTimer.Tick += CurrentTimeTimerTick;
        _currentTimeTimer.Start();
    }

    private void StopCurrentTimeTimer()
    {
        if (_currentTimeTimer == null) return;
        _currentTimeTimer.Tick -= CurrentTimeTimerTick;
        _currentTimeTimer.Stop();
        _currentTimeTimer = null;
    }

    private void CurrentTimeTimerTick(object? sender, EventArgs e)
    {
        if (!_previewSweepRunning) InvalidateVisual();
    }

    private static IBrush Brush(Color color) =>
        color == Colors.Transparent ? Brushes.Transparent : new SolidColorBrush(color);

    private static Color WithOpacity(Color color, double opacity)
    {
        byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * Math.Clamp(opacity, 0.0, 1.0)), 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static void DrawLine(DrawingContext context, Point a, Point b, Color color, double thickness)
    {
        if (Math.Abs(thickness - 1.0) < 0.001)
        {
            if (Math.Abs(a.Y - b.Y) < 0.001)
            {
                double y = Math.Round(a.Y) + 0.5;
                a = new Point(a.X, y);
                b = new Point(b.X, y);
            }
            else if (Math.Abs(a.X - b.X) < 0.001)
            {
                double x = Math.Round(a.X) + 0.5;
                a = new Point(x, a.Y);
                b = new Point(x, b.Y);
            }
        }

        context.DrawLine(new Pen(Brush(color), thickness), a, b);
    }

    private static void DrawDashedLine(
        DrawingContext context,
        Point a,
        Point b,
        Color color,
        double thickness,
        double dash,
        double gap)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.0) return;

        double ux = dx / length;
        double uy = dy / length;
        double cursor = 0.0;
        while (cursor < length)
        {
            double end = Math.Min(length, cursor + dash);
            Point p1 = new(a.X + ux * cursor, a.Y + uy * cursor);
            Point p2 = new(a.X + ux * end, a.Y + uy * end);
            DrawLine(context, p1, p2, color, thickness);
            cursor += dash + gap;
        }
    }

    private static FormattedText Text(string text, double size, Color color, bool monospace = false) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(monospace
                ? new FontFamily("Consolas, Cascadia Mono, Segoe UI")
                : new FontFamily("Segoe UI Variable, Segoe UI")),
            size,
            Brush(color));

    private static string FormatHourLabel(int hour, bool use24Hour)
    {
        if (use24Hour) return $"{hour:D2}:00";

        string suffix = hour is < 12 or 24 ? "am" : "pm";
        int display = hour % 12;
        if (display == 0) display = 12;
        return $"{display}{suffix}";
    }

    private static string FormatCursorTime(double t, bool use24Hour)
    {
        int totalMinutes = (int)Math.Round(t * 24.0 * 60.0);
        totalMinutes = Math.Clamp(totalMinutes, 0, 24 * 60);
        if (totalMinutes == 24 * 60) totalMinutes = 0;
        int hour = totalMinutes / 60;
        int minute = totalMinutes % 60;

        if (use24Hour) return $"{hour:D2}:{minute:D2}";

        (int displayHour, string suffix) = hour switch
        {
            0 => (12, "am"),
            < 12 => (hour, "am"),
            12 => (12, "pm"),
            _ => (hour - 12, "pm"),
        };
        return $"{displayHour}:{minute:D2}{suffix}";
    }

    private string FormatCursorValue(double value)
    {
        if (_offsetMode)
        {
            int offset = (int)Math.Round((value - 50.0) * 2.0);
            return offset > 0 ? $"+{offset}" : offset.ToString(CultureInfo.InvariantCulture);
        }

        return ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
    }

    private static bool SystemUses24HourClock() =>
        !CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h');

    private static string L(string key, string fallback)
    {
        try
        {
            string text = LocalizationManager.Instance[key];
            return string.IsNullOrWhiteSpace(text) || string.Equals(text, key, StringComparison.Ordinal)
                ? fallback
                : text;
        }
        catch
        {
            return fallback;
        }
    }
}
