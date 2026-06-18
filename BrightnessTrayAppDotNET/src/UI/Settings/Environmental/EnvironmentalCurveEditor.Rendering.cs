using System.Globalization;
using Avalonia;
using Avalonia.Media;
using BrightnessTrayAppDotNET.SunriseSunset;
using BrightnessTrayAppDotNET.Utils;

namespace BrightnessTrayAppDotNET.UI.Settings.Environmental;

public sealed partial class EnvironmentalCurveEditor
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        context.FillRectangle(Brushes.Transparent, bounds);

        Rect plot = PlotRect();
        if (plot.Width <= 0 || plot.Height <= 0) return;

        _exitPreviewButtonRect = null;
        _limitLabelHits.Clear();

        DrawSunOverlay(context, plot);
        DrawGrid(context, plot);
        DrawAxisLabels(context, plot, bounds);

        if (_showBrightness)
            DrawSeries(context, _brightness, Series.Brightness, _palette.BrightnessCurve, plot);
        if (_showNightLight)
            DrawSeries(context, _nightLight, Series.NightLight, _palette.NightLightCurve, plot);

        DrawOffsetHooks(context, plot);
        DrawBrightnessDegenerationHooks(context, plot);

        if (_disabledPeriodEnabled)
            DrawDisabledPeriod(context, plot);

        DrawCurrentTimeLine(context, plot);
        DrawCursorOverlay(context, plot);
        DrawSelectedNodeReadout(context, plot);

        if (_previewMode)
            DrawPreviewOverlay(context, bounds, plot);
    }

    private void DrawSunOverlay(DrawingContext context, Rect plot)
    {
        if (!_showSunOverlay) return;
        if ((_latitude == 0.0 && _longitude == 0.0)
            || _latitude is < -90.0 or > 90.0
            || _longitude is < -180.0 or > 180.0)
            return;

        if (!TryGetSunOverlay(out SunTimes sun)) return;

        double? sunrise = ToDayFraction(sun.Sunrise);
        double? sunset = ToDayFraction(sun.Sunset);
        double? astroDawn = ToDayFraction(sun.Twilight?.AstronomicalDawn);
        double? astroDusk = ToDayFraction(sun.Twilight?.AstronomicalDusk);

        if (sunrise is null && sunset is null)
        {
            DrawWholeDayPolarOverlay(context, plot);
            return;
        }

        if (sunrise is not { } sr || sunset is not { } ss) return;

        if (sr <= ss)
        {
            if (astroDawn is { } dawn)
            {
                AddOverlayBand(context, 0.0, dawn, _palette.NightBackdrop, plot);
                AddOverlayBand(context, dawn, sr, _palette.TwilightBackdrop, plot);
            }
            else
                AddOverlayBand(context, 0.0, sr, _palette.TwilightBackdrop, plot);

            if (astroDusk is { } dusk)
            {
                AddOverlayBand(context, ss, dusk, _palette.TwilightBackdrop, plot);
                AddOverlayBand(context, dusk, 1.0, _palette.NightBackdrop, plot);
            }
            else
                AddOverlayBand(context, ss, 1.0, _palette.TwilightBackdrop, plot);
        }
        else
            AddOverlayBand(context, ss, sr, _palette.TwilightBackdrop, plot);
    }

    private void DrawWholeDayPolarOverlay(DrawingContext context, Rect plot)
    {
        if (!TryGetSunOverlayNoonPosition(out SolarPosition? noonPosition)) return;
        if (noonPosition == null || noonPosition.Elevation > 0.0) return;
        AddOverlayBand(
            context,
            0.0,
            1.0,
            noonPosition.Elevation > -18.0 ? _palette.TwilightBackdrop : _palette.NightBackdrop,
            plot);
    }

    private bool TryGetSunOverlay(out SunTimes sun)
    {
        SunOverlayCacheKey key = CurrentSunOverlayCacheKey();
        if (_sunOverlayCacheKey == key)
        {
            sun = _sunOverlayCache!;
            return !_sunOverlayCacheFailed && _sunOverlayCache != null;
        }

        _sunOverlayCacheKey = key;
        _sunOverlayCache = null;
        _sunOverlayCacheFailed = false;
        _sunOverlayNoonPositionCache = null;
        _sunOverlayNoonPositionCached = false;
        try
        {
            _sunOverlayCache = SPACalculator.GetSunTimes(_latitude, _longitude, GetOverlayReferenceTime(key.Date));
            sun = _sunOverlayCache;
            return true;
        }
        catch
        {
            _sunOverlayCacheFailed = true;
            sun = null!;
            return false;
        }
    }

    private bool TryGetSunOverlayNoonPosition(out SolarPosition? noonPosition)
    {
        _ = TryGetSunOverlay(out _);
        if (_sunOverlayCacheFailed)
        {
            noonPosition = null;
            return false;
        }

        if (_sunOverlayNoonPositionCached)
        {
            noonPosition = _sunOverlayNoonPositionCache;
            return true;
        }

        try
        {
            SunOverlayCacheKey key = CurrentSunOverlayCacheKey();
            _sunOverlayNoonPositionCache =
                SPACalculator.GetSolarPosition(_latitude, _longitude, GetOverlayReferenceTime(key.Date));
            _sunOverlayNoonPositionCached = true;
            noonPosition = _sunOverlayNoonPositionCache;
            return true;
        }
        catch
        {
            _sunOverlayNoonPositionCached = true;
            _sunOverlayNoonPositionCache = null;
            noonPosition = null;
            return false;
        }
    }

    private SunOverlayCacheKey CurrentSunOverlayCacheKey() =>
        new(_latitude, _longitude, _useDaylightSavings, (_sunOverlayDate ?? DateTime.Today).Date);

    private static void AddOverlayBand(DrawingContext context, double startT, double endT, Color color, Rect plot)
    {
        if (endT <= startT) return;
        double x1 = ScreenX(Math.Clamp(startT, 0.0, 1.0), plot);
        double x2 = ScreenX(Math.Clamp(endT, 0.0, 1.0), plot);
        if (x2 <= x1) return;
        context.FillRectangle(Brush(color), new Rect(x1, plot.Top, x2 - x1, plot.Height));
    }

    private DateTimeOffset GetOverlayReferenceTime(DateTime date)
    {
        DateTime localNoon = DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Unspecified);
        TimeSpan offset = _useDaylightSavings
            ? TimeZoneInfo.Local.GetUtcOffset(localNoon)
            : TimeZoneInfo.Local.BaseUtcOffset;
        return new DateTimeOffset(localNoon, offset);
    }

    private static double? ToDayFraction(DateTimeOffset? when)
    {
        if (when is not { } t) return null;
        double hours = t.DateTime.TimeOfDay.TotalHours;
        if (hours is < 0.0 or > 24.0) return null;
        return Math.Clamp(hours / 24.0, 0.0, 1.0);
    }

    private void DrawGrid(DrawingContext context, Rect plot)
    {
        for (int i = 0; i <= VerticalGridDivisions; i++)
        {
            double value = 100.0 * (VerticalGridDivisions - i) / VerticalGridDivisions;
            double y = ScreenY(value, plot);
            bool zeroLine = _offsetMode && i == VerticalGridDivisions / 2;
            DrawLine(
                context,
                new Point(plot.Left, y),
                new Point(plot.Right, y),
                WithOpacity(_palette.GridLine, zeroLine ? 0.85 : 0.4),
                1.0);
        }

        for (int i = 0; i <= HorizontalGridDivisions; i++)
        {
            double x = ScreenX((double)i / HorizontalGridDivisions, plot);
            DrawLine(
                context,
                new Point(x, plot.Top),
                new Point(x, plot.Bottom),
                WithOpacity(_palette.GridLine, 0.4),
                1.0);
        }
    }

    private void DrawAxisLabels(DrawingContext context, Rect plot, Rect bounds)
    {
        bool use24Hour = SystemUses24HourClock();
        for (int i = 0; i <= HorizontalGridDivisions; i++)
        {
            int hour = (int)Math.Round(24.0 * i / HorizontalGridDivisions);
            string text = FormatHourLabel(hour, use24Hour);
            FormattedText formatted = Text(text, TimeAxisLabelFontSize, WithOpacity(_palette.SecondaryForeground, 0.7));
            double x = ScreenX((double)i / HorizontalGridDivisions, plot) - formatted.Width / 2.0;
            double y = bounds.Height - TimeAxisHeight + 2.0;
            context.DrawText(formatted, new Point(x, y));
        }

        for (int i = 0; i <= VerticalGridDivisions; i++)
        {
            int value = _offsetMode
                ? 100 - (int)Math.Round(200.0 * i / VerticalGridDivisions)
                : 100 - (int)Math.Round(100.0 * i / VerticalGridDivisions);
            string text = _offsetMode && value > 0 ? $"+{value}" : value.ToString(CultureInfo.InvariantCulture);
            double y = ScreenY(100.0 * (VerticalGridDivisions - i) / VerticalGridDivisions, plot);
            FormattedText left = Text(text, TimeAxisLabelFontSize, WithOpacity(_palette.SecondaryForeground, 0.7));
            context.DrawText(left, new Point(AxisGutterWidth - left.Width - 2.0, y - left.Height / 2.0));

            FormattedText right = Text(text, TimeAxisLabelFontSize, WithOpacity(_palette.SecondaryForeground, 0.7));
            context.DrawText(right, new Point(bounds.Width - AxisGutterWidth + 2.0, y - right.Height / 2.0));
        }
    }

    private void DrawSeries(
        DrawingContext context,
        List<EnvironmentalCurvePoint> points,
        Series series,
        Color color,
        Rect plot)
    {
        if (points.Count == 0) return;

        List<EnvironmentalCurvePoint> ordered = [.. points.OrderBy(p => p.Time)];
        if (ordered.Count >= 2)
        {
            double[] xs = new double[ordered.Count];
            double[] ys = new double[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                xs[i] = ordered[i].Time;
                ys[i] = ordered[i].Value;
            }

            double[] tangents = EnvironmentalCurveSampler.ComputeMonotonicTangents(xs, ys);
            int samples = Math.Max(2, (int)Math.Ceiling(plot.Width));
            StreamGeometry geometry = new();
            using (StreamGeometryContext geometryContext = geometry.Open())
            {
                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / (samples - 1);
                    double linear = EnvironmentalCurveSampler.InterpolateLinear(xs, ys, t);
                    double cubic = EnvironmentalCurveSampler.InterpolateMonotonicCubic(xs, ys, tangents, t);
                    double v = linear + (cubic - linear) * _smoothness;
                    Point current = new(ScreenX(t, plot), ScreenY(v, plot));
                    if (i == 0)
                        geometryContext.BeginFigure(current, isFilled: false);
                    else
                        geometryContext.LineTo(current);
                }
            }

            context.DrawGeometry(null, new Pen(Brush(color), 2.0), geometry);
        }

        foreach (EnvironmentalCurvePoint point in ordered)
        {
            Point center = PointFor(point, plot);
            bool active =
                (_hoveredThumb is { } hovered && hovered.Series == series && ReferenceEquals(hovered.Point, point)) ||
                (_dragPoint != null && _dragSeries == series && ReferenceEquals(_dragPoint, point));
            bool selected =
                _selectedPoint != null && _selectedSeries == series && ReferenceEquals(_selectedPoint, point);
            DrawThumb(context, center, color, active, selected);
        }
    }

    private void DrawOffsetHooks(DrawingContext context, Rect plot)
    {
        if (!_offsetMode || _curveData == null) return;

        List<(Series Series, LimitKind Kind, double LineY, bool Active)> labelSpecs = [];

        if (_showBrightness)
        {
            DrawLimitLine(context, Series.Brightness, LimitKind.Min, _curveData.BrightnessOffsetMin, plot, labelSpecs);
            DrawLimitLine(context, Series.Brightness, LimitKind.Max, _curveData.BrightnessOffsetMax, plot, labelSpecs);
        }

        if (_showNightLight)
        {
            DrawLimitLine(context, Series.NightLight, LimitKind.Min, _curveData.NightLightOffsetMin, plot, labelSpecs);
            DrawLimitLine(context, Series.NightLight, LimitKind.Max, _curveData.NightLightOffsetMax, plot, labelSpecs);
        }

        DrawLimitLabels(context, plot, labelSpecs);
    }

    private void DrawLimitLine(
        DrawingContext context,
        Series series,
        LimitKind kind,
        double value,
        Rect plot,
        List<(Series Series, LimitKind Kind, double LineY, bool Active)> labelSpecs)
    {
        double y = ScreenY(value, plot);
        bool active =
            (_hoveredLimit is { } hovered && hovered.Series == series && hovered.Kind == kind) ||
            (_draggingLimit && _limitDragSeries == series && _limitDragKind == kind);
        Color color = WithOpacity(_palette.Foreground, active ? 1.0 : 0.7);
        DrawDashedLine(context, new Point(plot.Left, y), new Point(plot.Right, y), color, 1.5, 4.0, 3.0);
        labelSpecs.Add((series, kind, y, active));
    }

    private void DrawLimitLabels(
        DrawingContext context,
        Rect plot,
        List<(Series Series, LimitKind Kind, double LineY, bool Active)> specs)
    {
        if (specs.Count == 0) return;

        const double gap = 2.0;
        const double horizontalGap = 6.0;
        double plotMid = plot.Center.X;

        List<(Series Series, LimitKind Kind, FormattedText Text, double Left, double Top, double Width, double Height)>
            entries = [];
        foreach ((Series series, LimitKind kind, double lineY, bool active) in specs)
        {
            string label = (series, kind) switch
            {
                (Series.Brightness, LimitKind.Min) => L("Settings_CurveEditor_LimitLabel_MinBrightness",
                    "Min brightness"),
                (Series.Brightness, LimitKind.Max) => L("Settings_CurveEditor_LimitLabel_MaxBrightness",
                    "Max brightness"),
                (Series.NightLight, LimitKind.Min) => L("Settings_CurveEditor_LimitLabel_MinNightLight",
                    "Min night light"),
                _ => L("Settings_CurveEditor_LimitLabel_MaxNightLight", "Max night light"),
            };
            Color color = WithOpacity(_palette.Foreground, active ? 1.0 : 0.7);
            FormattedText text = Text(label, TimeAxisLabelFontSize, color);
            double left = plotMid - text.Width / 2.0;
            double top = kind == LimitKind.Min ? lineY - text.Height - gap : lineY + gap;
            entries.Add((series, kind, text, left, top, text.Width, text.Height));
        }

        bool[] visited = new bool[entries.Count];
        for (int seed = 0; seed < entries.Count; seed++)
        {
            if (visited[seed]) continue;

            List<int> cluster = [seed];
            visited[seed] = true;
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (visited[i]) continue;
                    foreach (int clustered in cluster)
                    {
                        if (!VerticalBandsOverlap(entries[i].Top, entries[i].Height, entries[clustered].Top,
                                entries[clustered].Height))
                            continue;

                        cluster.Add(i);
                        visited[i] = true;
                        grew = true;
                        break;
                    }
                }
            }

            if (cluster.Count <= 1) continue;

            cluster.Sort((a, b) =>
            {
                int series = entries[a].Series.CompareTo(entries[b].Series);
                return series != 0 ? series : entries[a].Kind.CompareTo(entries[b].Kind);
            });

            double totalWidth = cluster.Sum(index => entries[index].Width) + (cluster.Count - 1) * horizontalGap;
            double cursor = plotMid - totalWidth / 2.0;
            foreach (int index in cluster)
            {
                (Series series, LimitKind kind, FormattedText text, double left, double top, double width,
                    double height) = entries[index];
                entries[index] = (series, kind, text, cursor, top, width, height);
                cursor += width + horizontalGap;
            }
        }

        foreach ((Series series, LimitKind kind, FormattedText text, double left, double top, double width,
                     double height) in entries)
        {
            double x = Math.Clamp(left, plot.Left, Math.Max(plot.Left, plot.Right - width));
            double y = Math.Clamp(top, plot.Top - DisabledPeriodPinAreaHeight,
                Math.Max(plot.Top, plot.Bottom - height));
            context.DrawText(text, new Point(x, y));
            _limitLabelHits.Add(new LimitLabelHit(series, kind, new Rect(x - 3.0, y - 2.0, width + 6.0, height + 4.0)));
        }
    }

    private static bool VerticalBandsOverlap(double topA, double heightA, double topB, double heightB) =>
        topA < topB + heightB && topB < topA + heightA;

    private void DrawBrightnessDegenerationHooks(DrawingContext context, Rect plot)
    {
        if (_curveData == null || !_showBrightness) return;
        if (_activeMinBrightness is not { } minB || _activeMaxBrightness is not { } maxB) return;

        double? upperSample;
        double? lowerSample;
        if (_offsetMode)
        {
            upperSample = 50.0 + (100.0 - maxB) / 2.0;
            lowerSample = 50.0 + (0.0 - minB) / 2.0;
        }
        else
        {
            double gap = Math.Max(0.0, maxB - minB);
            upperSample = 100.0 - gap;
            lowerSample = gap;
        }

        DrawDegenerationLine(context, upperSample.Value, LimitKind.Max, plot);
        DrawDegenerationLine(context, lowerSample.Value, LimitKind.Min, plot);
    }

    private void DrawDegenerationLine(DrawingContext context, double sample, LimitKind kind, Rect plot)
    {
        if (sample is < 0.0 or > 100.0) return;
        double y = ScreenY(sample, plot);
        Color color = WithOpacity(_palette.SecondaryForeground, 0.45);
        DrawDashedLine(context, new Point(plot.Left, y), new Point(plot.Right, y), color, 1.0, 1.0, 2.5);

        string label = kind == LimitKind.Max
            ? L("Settings_CurveEditor_DegenerationLabel_UpperBrightnessOffset", "Upper brightness offset limit")
            : L("Settings_CurveEditor_DegenerationLabel_LowerBrightnessOffset", "Lower brightness offset limit");
        FormattedText text = Text(label, TimeAxisLabelFontSize, color);
        double textY = kind == LimitKind.Min ? y - text.Height - 2.0 : y + 2.0;
        context.DrawText(text, new Point(plot.Center.X - text.Width / 2.0, textY));
    }

    private void DrawDisabledPeriod(DrawingContext context, Rect plot)
    {
        if (_disabledPeriodStart <= _disabledPeriodEnd)
            DrawDisabledBand(context, _disabledPeriodStart, _disabledPeriodEnd, plot);
        else
        {
            DrawDisabledBand(context, _disabledPeriodStart, 1.0, plot);
            DrawDisabledBand(context, 0.0, _disabledPeriodEnd, plot);
        }

        double pinY = TopInset / 2.0;
        DrawDisabledPin(context, DisabledPin.Start, _disabledPeriodStart, pinY, plot);
        DrawDisabledPin(context, DisabledPin.End, _disabledPeriodEnd, pinY, plot);
    }

    private void DrawDisabledBand(DrawingContext context, double startT, double endT, Rect plot)
    {
        if (endT <= startT) return;
        double x1 = ScreenX(startT, plot);
        double x2 = ScreenX(endT, plot);
        context.FillRectangle(Brush(_palette.DisabledBand), new Rect(x1, plot.Top, x2 - x1, plot.Height));
    }

    private void DrawDisabledPin(DrawingContext context, DisabledPin pin, double t, double pinY, Rect plot)
    {
        double x = ScreenX(t, plot);
        DrawLine(
            context,
            new Point(x, pinY + ThumbSize / 2.0),
            new Point(x, plot.Bottom),
            WithOpacity(_palette.SecondaryForeground, 0.5),
            1.0);

        bool active =
            (_hoveredDisabledPin is { } hovered && hovered == pin) ||
            (_dragDisabledPin is { } dragging && dragging == pin);
        DrawThumb(context, new Point(x, pinY), _palette.Foreground, active, selected: false);
    }

    private void DrawCurrentTimeLine(DrawingContext context, Rect plot)
    {
        double t = _previewSweepRunning ? _previewSweepCursor : EnvironmentalCurveSampler.CurrentDayFraction();
        double x = ScreenX(Math.Clamp(t, 0.0, 1.0), plot);
        DrawDashedLine(
            context,
            new Point(x, plot.Top),
            new Point(x, plot.Bottom),
            WithOpacity(_palette.CurrentTime, 0.85),
            1.25,
            3.0,
            2.0);
    }

    private void DrawCursorOverlay(DrawingContext context, Rect plot)
    {
        if (_cursorPos is not { } cursor) return;
        if (!plot.Contains(cursor)) return;

        double t = Math.Clamp(FromScreenX(cursor.X, plot), 0.0, 1.0);
        double v = Math.Clamp(FromScreenY(cursor.Y, plot), 0.0, 100.0);
        string readout = $"{FormatCursorTime(t, SystemUses24HourClock())}  {FormatCursorValue(v)}";
        FormattedText text = Text(readout, 12.0, _palette.Foreground, monospace: true);
        Rect pill = ReadoutRect(text, plot, cursor, avoidCursor: true, verticalSlot: 0);
        DrawPill(context, pill, text, _palette.CardBackground, _palette.Foreground);

        if (!_showCursorReadout) return;

        double x = ScreenX(t, plot);
        DrawDashedLine(
            context,
            new Point(x, plot.Top),
            new Point(x, plot.Bottom),
            WithOpacity(_palette.SecondaryForeground, 0.6),
            1.0,
            2.0,
            3.0);
        DrawCursorMarker(context, _brightness, _showBrightness, t, _palette.BrightnessCurve, plot);
        DrawCursorMarker(context, _nightLight, _showNightLight, t, _palette.NightLightCurve, plot);
    }

    private void DrawCursorMarker(
        DrawingContext context,
        List<EnvironmentalCurvePoint> series,
        bool visible,
        double t,
        Color color,
        Rect plot)
    {
        if (!visible) return;

        double sample = SampleCurveAt(series, t);
        if (double.IsNaN(sample)) return;

        Point center = new(ScreenX(t, plot), ScreenY(sample, plot));
        context.DrawEllipse(Brush(color), new Pen(Brush(_palette.Foreground)), center, 4.0, 4.0);

        FormattedText label = Text(FormatCursorValue(sample), 11.0, color, monospace: true);
        const double gap = 6.0;
        double left = SampleCurveAt(series, Math.Max(0.0, t - 0.01));
        double right = SampleCurveAt(series, Math.Min(1.0, t + 0.01));
        double slope = (double.IsNaN(left) || double.IsNaN(right)) ? 0.0 : right - left;
        double x = slope > 0 ? center.X - gap - label.Width : center.X + gap;
        double y = center.Y - label.Height - 2.0;
        if (y < plot.Top) y = center.Y + 5.0;
        x = Math.Clamp(x, plot.Left, Math.Max(plot.Left, plot.Right - label.Width));
        y = Math.Clamp(y, plot.Top, Math.Max(plot.Top, plot.Bottom - label.Height));
        context.DrawText(label, new Point(x, y));
    }

    private void DrawSelectedNodeReadout(DrawingContext context, Rect plot)
    {
        if (_selectedPoint == null) return;

        string textValue = string.Format(
            CultureInfo.CurrentCulture,
            L("Settings_CurveEditor_NodeReadout_Format", "{0}  {1}"),
            FormatCursorTime(_selectedPoint.Time, SystemUses24HourClock()),
            FormatCursorValue(_selectedPoint.Value));
        Color color = _selectedSeries == Series.Brightness ? _palette.BrightnessCurve : _palette.NightLightCurve;
        FormattedText text = Text(textValue, 12.0, color, monospace: true);
        Rect pill = ReadoutRect(text, plot, _cursorPos ?? new Point(plot.Right, plot.Top), avoidCursor: false,
            verticalSlot: 1);
        DrawPill(context, pill, text, _palette.CardBackground, color);
    }

    private void DrawPreviewOverlay(DrawingContext context, Rect bounds, Rect plot)
    {
        context.FillRectangle(Brush(_palette.PreviewTint), bounds, 6);

        string label = L("Settings_CurveEditor_ExitPreviewMode_Button", "Exit preview");
        FormattedText text = Text(label, 12.0, _palette.Foreground);
        Rect button = new(
            plot.Right - text.Width - 28.0,
            plot.Bottom - text.Height - 20.0,
            text.Width + 20.0,
            text.Height + 10.0);
        _exitPreviewButtonRect = button;
        context.FillRectangle(Brush(WithOpacity(_palette.CardBackground, 0.92)), button, 4);
        context.DrawRectangle(new Pen(Brush(WithOpacity(_palette.Foreground, 0.4))), button, 4);
        context.DrawText(text, new Point(button.X + 10.0, button.Y + 5.0));
    }

    private static Rect ReadoutRect(FormattedText text, Rect plot, Point cursor, bool avoidCursor, int verticalSlot)
    {
        double width = text.Width + 12.0;
        double height = text.Height + 4.0;
        bool useLeft = avoidCursor && cursor.Y < plot.Top + 24.0 && cursor.X > plot.Right - 100.0;
        double x = useLeft ? plot.Left : plot.Right - width;
        double y = plot.Top + verticalSlot * (height + 2.0);
        return new Rect(x, y, width, height);
    }

    private static void DrawPill(DrawingContext context, Rect rect, FormattedText text, Color background, Color border)
    {
        context.FillRectangle(Brush(WithOpacity(background, 0.88)), rect, 3);
        context.DrawRectangle(new Pen(Brush(WithOpacity(border, 0.22))), rect, 3);
        context.DrawText(text, new Point(rect.X + 6.0, rect.Y + 2.0));
    }

    private void DrawThumb(DrawingContext context, Point center, Color fill, bool active, bool selected)
    {
        double stroke = selected ? 1.5 : active ? 1.25 : 0.0;
        Pen? ring = stroke > 0.0 ? new Pen(Brush(_palette.Foreground), stroke) : null;
        double radius = stroke > 0.0
            ? Math.Max(0.0, ThumbSize / 2.0 - stroke / 2.0)
            : ThumbSize / 2.0;
        context.DrawEllipse(Brush(fill), ring, center, radius, radius);
    }
}
