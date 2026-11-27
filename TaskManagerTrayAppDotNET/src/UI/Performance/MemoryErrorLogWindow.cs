using static TaskManagerTrayAppDotNET.UI.Performance.PerformanceText;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>Modal list of the memory errors Windows recorded in the System event log, newest first.</summary>
/// <remarks>The list follows the monitor while open. Errors at or before the last Reset stay listed, dimmed.</remarks>
internal sealed class MemoryErrorLogWindow : Window
{
    private readonly MemoryErrorLogMonitor _monitor;
    private readonly Func<MemoryErrorAcknowledgement> _getAcknowledgement;
    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly TextBlock _summary;
    private readonly StackPanel _entries;

    public MemoryErrorLogWindow(
        MemoryErrorLogMonitor monitor,
        Func<MemoryErrorAcknowledgement> getAcknowledgement,
        SettingsPalette palette)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(getAcknowledgement);
        ArgumentNullException.ThrowIfNull(palette);

        _monitor = monitor;
        _getAcknowledgement = getAcknowledgement;
        _palette = palette;
        _resources = TaskManagerWindowResources.Current;
        Title = L(nameof(AppStrings.Perf_MemoryErrors_LogTitle));
        Width = _resources.AxamlMemoryErrorLog.WindowWidth;
        Height = _resources.AxamlMemoryErrorLog.WindowHeight;
        MinWidth = _resources.AxamlMemoryErrorLog.WindowMinWidth;
        MinHeight = _resources.AxamlMemoryErrorLog.WindowMinHeight;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        this.MapTo(ControlMap.MemoryErrorLog.ID);
        this.MapCommand(ControlMap.MemoryErrorLog.Dismiss, (Action)Close);

        _summary = TrayAppDotNETSettingsUI.Text(
            string.Empty,
            palette,
            _resources.AxamlMemoryErrorLog.SummaryFontSize);
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.Margin = _resources.AxamlMemoryErrorLog.SummaryMargin;

        _entries = new StackPanel { Spacing = _resources.AxamlMemoryErrorLog.EntrySpacing };
        ScrollViewer entriesScroll = new ScrollViewer
        {
            Content = _entries,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }.MapTo(ControlMap.MemoryErrorLog.Entries);
        Grid.SetRow(entriesScroll, value: 1);

        SettingsButton closeButton = TrayAppDotNETSettingsUI.Button(
                text: L(nameof(CommonStrings.Common_Close)),
                palette)
            .MapTo(ControlMap.MemoryErrorLog.Close);
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        closeButton.Margin = _resources.AxamlMemoryErrorLog.ActionsMargin;
        closeButton.Click += OnCloseClick;
        Grid.SetRow(closeButton, value: 2);

        Content = new Grid
        {
            Margin = _resources.AxamlMemoryErrorLog.ContentMargin,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Children = { _summary, entriesScroll, closeButton }
        };

        _monitor.Changed += Rebuild;
        Closed += (_, _) => _monitor.Changed -= Rebuild;
        Rebuild();
    }

    private void OnCloseClick(object? sender, EventArgs eventArgs) => Close();

    private void Rebuild()
    {
        IReadOnlyList<MemoryErrorEntry> entries = _monitor.Entries;
        MemoryErrorAcknowledgement acknowledgement = _getAcknowledgement();
        _summary.Text = BuildSummary(entries, acknowledgement, _monitor.Error);
        _entries.Children.Clear();
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            MemoryErrorEntry entry = entries[entryIndex];
            _entries.Children.Add(BuildEntry(entry, acknowledgement.Covers(entry)));
        }
    }

    /// <summary>States the counts by kind, what Reset does, and any read failure.</summary>
    internal static string BuildSummary(
        IReadOnlyList<MemoryErrorEntry> entries,
        MemoryErrorAcknowledgement acknowledgement,
        string? error)
    {
        if (error != null) return F(nameof(AppStrings.Perf_MemoryErrors_ReadFailedFormat), error);
        if (entries.Count == 0) return L(nameof(AppStrings.Perf_MemoryErrors_None));

        int corrected = 0;
        int uncorrected = 0;
        int diagnosticFailures = 0;
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            MemoryErrorEntry entry = entries[entryIndex];
            if (acknowledgement.Covers(entry)) continue;

            switch (entry.Kind)
            {
                case MemoryErrorKind.Corrected:
                    corrected++;
                    break;
                case MemoryErrorKind.Uncorrected:
                    uncorrected++;
                    break;
                case MemoryErrorKind.DiagnosticFailure:
                    diagnosticFailures++;
                    break;
            }
        }

        return F(
            nameof(AppStrings.Perf_MemoryErrors_SummaryFormat),
            corrected,
            uncorrected,
            diagnosticFailures,
            entries.Count);
    }

    private Border BuildEntry(MemoryErrorEntry entry, bool isAcknowledged)
    {
        string heading = F(
            nameof(AppStrings.Perf_MemoryErrors_EntryHeadingFormat),
            KindText(entry.Kind),
            entry.TimeCreatedUtc.ToLocalTime().ToString(format: "G", CultureInfo.CurrentCulture),
            entry.EventID);
        if (isAcknowledged)
            heading = F(nameof(AppStrings.Perf_MemoryErrors_AcknowledgedFormat), heading);

        TextBlock headingText = TrayAppDotNETSettingsUI.Text(
            heading,
            _palette,
            _resources.AxamlMemoryErrorLog.EntryHeadingFontSize,
            (FontWeight)_resources.AxamlMemoryErrorLog.EntryHeadingFontWeight);
        headingText.TextWrapping = TextWrapping.Wrap;
        SelectableTextBlock message = new()
        {
            Text = entry.Message.Length > 0 ? entry.Message : L(nameof(AppStrings.Perf_MemoryErrors_NoMessage)),
            TextWrapping = TextWrapping.Wrap,
            FontSize = _resources.AxamlMemoryErrorLog.EntryMessageFontSize,
            Foreground = TrayAppDotNETSettingsUI.Brush(_palette.Foreground),
            Margin = _resources.AxamlMemoryErrorLog.EntryMessageMargin
        };

        return new Border
        {
            Padding = _resources.AxamlMemoryErrorLog.EntryPadding,
            CornerRadius = _resources.AxamlMemoryErrorLog.EntryCornerRadius,
            BorderThickness = _resources.AxamlMemoryErrorLog.EntryBorderThickness,
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            Background = !isAcknowledged && entry.Kind != MemoryErrorKind.Corrected
                ? TrayAppDotNETSettingsUI.Brush(_resources.AxamlTaskManagerDetails.SearchInvalidTintColor)
                : Brushes.Transparent,
            Opacity = isAcknowledged ? _resources.AxamlMemoryErrorLog.AcknowledgedOpacity : 1,
            Child = new StackPanel { Children = { headingText, message } }
        };
    }

    private static string KindText(MemoryErrorKind kind) => kind switch
    {
        MemoryErrorKind.Corrected => L(nameof(AppStrings.Perf_MemoryErrors_KindCorrected)),
        MemoryErrorKind.Uncorrected => L(nameof(AppStrings.Perf_MemoryErrors_KindUncorrected)),
        MemoryErrorKind.DiagnosticFailure => L(nameof(AppStrings.Perf_MemoryErrors_KindDiagnosticFailure)),
        _ => kind.ToString()
    };
}
