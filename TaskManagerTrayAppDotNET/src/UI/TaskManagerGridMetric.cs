using System.Globalization;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Names the table typography value a zoom or stretch gesture just set.</summary>
internal enum TaskManagerGridMetricKind : byte
{
    Zoom,
    Stretch,
    ZoomBaseline,
    StretchBaseline
}

/// <summary>A table typography value a zoom or stretch gesture just set, as the sidebar indicator shows it.</summary>
/// <param name="Kind">What the gesture changed; a baseline kind made the current value the reset target.</param>
/// <param name="Value">The grid font size for the zoom kinds, the row spacing for the stretch kinds.</param>
internal readonly record struct TaskManagerGridMetric(TaskManagerGridMetricKind Kind, double Value)
{
    /// <summary>Gets the value relative to the app's default font size or row spacing, in percent.</summary>
    public double Percent =>
        Value / (IsZoom ? AppSettings.GridFontSizeDefault : AppSettings.GridRowSpacingDefault) * 100;

    private bool IsZoom => Kind is TaskManagerGridMetricKind.Zoom or TaskManagerGridMetricKind.ZoomBaseline;

    /// <summary>Formats <see cref="Percent"/> as a whole percentage, for example "108%".</summary>
    public string FormatPercent(CultureInfo culture) =>
        string.Concat(Percent.ToString(format: "N0", culture), "%");
}
