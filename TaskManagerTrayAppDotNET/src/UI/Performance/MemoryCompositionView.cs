using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>Displays the in-use, modified, standby, and free physical-memory composition.</summary>
internal sealed class MemoryCompositionView : StackPanel
{
    private const ulong BytesPerMebibyte = 1_048_576;

    private readonly TaskManagerWindowResources _resources;
    private readonly ColumnDefinition _inUseColumn = new();
    private readonly ColumnDefinition _modifiedColumn = new();
    private readonly ColumnDefinition _standbyColumn = new();
    private readonly ColumnDefinition _freeColumn = new();
    private readonly Border _modifiedSegment;
    private readonly Border _standbySegment;
    private readonly Border _freeSegment;
    private readonly TextBlock _inUseTooltip;
    private readonly TextBlock _modifiedTooltip;
    private readonly TextBlock _standbyTooltip;
    private readonly TextBlock _freeTooltip;

    public MemoryCompositionView(
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        _resources = resources;
        IsVisible = false;
        Margin = resources.AxamlTaskManagerPerformance.MemoryCompositionMargin;

        TextBlock label = TrayAppDotNETSettingsUI.Text(
            text: "Memory composition",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        label.Margin = resources.AxamlTaskManagerPerformance.MemoryCompositionLabelMargin;
        Children.Add(label);

        Color accent = PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.Memory);
        SolidColorBrush accentBrush = new(accent);
        SolidColorBrush inUseBrush = new(accent)
        {
            Opacity = resources.AxamlTaskManagerPerformance.MemoryCompositionFillOpacity
        };
        SolidColorBrush modifiedBrush = new(accent)
        {
            Opacity = resources.AxamlTaskManagerPerformance.MemoryCompositionModifiedOpacity
        };
        Border inUseSegment = CreateSegment(
            inUseBrush,
            accentBrush,
            resources.AxamlTaskManagerPerformance.MemoryCompositionSegmentBorderThickness);
        _modifiedSegment = CreateSegment(
            modifiedBrush,
            accentBrush,
            resources.AxamlTaskManagerPerformance.MemoryCompositionSegmentBorderThickness);
        _standbySegment = CreateSegment(
            Brushes.Transparent,
            accentBrush,
            resources.AxamlTaskManagerPerformance.MemoryCompositionSegmentBorderThickness);
        _freeSegment = CreateSegment(
            Brushes.Transparent,
            accentBrush,
            borderThickness: default);
        _inUseTooltip = CreateTooltip();
        _modifiedTooltip = CreateTooltip();
        _standbyTooltip = CreateTooltip();
        _freeTooltip = CreateTooltip();
        TrayAppDotNETToolTip.SetTip(inUseSegment, _inUseTooltip);
        TrayAppDotNETToolTip.SetTip(_modifiedSegment, _modifiedTooltip);
        TrayAppDotNETToolTip.SetTip(_standbySegment, _standbyTooltip);
        TrayAppDotNETToolTip.SetTip(_freeSegment, _freeTooltip);

        Grid segments = new()
        {
            ColumnDefinitions = { _inUseColumn, _modifiedColumn, _standbyColumn, _freeColumn },
            Children = { inUseSegment, _modifiedSegment, _standbySegment, _freeSegment }
        };
        Grid.SetColumn(_modifiedSegment, value: 1);
        Grid.SetColumn(_standbySegment, value: 2);
        Grid.SetColumn(_freeSegment, value: 3);

        Border frame = new()
        {
            Height = resources.AxamlTaskManagerPerformance.MemoryCompositionHeight,
            BorderBrush = accentBrush,
            BorderThickness = resources.AxamlTaskManagerPerformance.MemoryCompositionFrameBorderThickness,
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            Child = segments
        };
        Children.Add(frame);
    }

    /// <summary>Updates segment widths and the segment-specific explanatory tooltips.</summary>
    public void Update(MemoryPerformanceSnapshot memory)
    {
        MemoryCompositionSnapshot composition = memory.Composition;
        ulong inUseBytes = memory.UsedPhysicalBytes;
        ulong modifiedBytes = composition.HasCompositionData ? composition.ModifiedBytes : 0;
        ulong standbyBytes = composition.HasCompositionData
            ? composition.StandbyBytes
            : memory.AvailablePhysicalBytes;
        ulong freeBytes = composition.HasCompositionData ? composition.FreeBytes : 0;
        if (memory.TotalPhysicalBytes == 0)
            inUseBytes = 1;

        _inUseColumn.Width = Star(inUseBytes);
        _modifiedColumn.Width = Star(modifiedBytes);
        _standbyColumn.Width = Star(standbyBytes);
        _freeColumn.Width = Star(freeBytes);
        _modifiedSegment.IsVisible = modifiedBytes > 0;
        _standbySegment.IsVisible = standbyBytes > 0;
        _freeSegment.IsVisible = freeBytes > 0;

        _inUseTooltip.Text = BuildInUseTooltip(memory);
        _modifiedTooltip.Text = string.Concat(
            str0: "Modified (",
            FormatMebibytes(modifiedBytes),
            str2: " MB)\nMemory whose contents must be written to disk before it can be used for another purpose");
        string standbyTitle = composition.HasCompositionData ? "Standby" : "Available";
        string standbyDescription = composition.HasCompositionData
            ? "Memory that contains cached data and code that is not actively in use"
            : "Memory that can be given immediately to processes, drivers, or the operating system";
        _standbyTooltip.Text = string.Concat(
            standbyTitle,
            " (",
            FormatMebibytes(standbyBytes),
            " MB)\n",
            standbyDescription);
        _freeTooltip.Text = string.Concat(
            str0: "Free (",
            FormatMebibytes(freeBytes),
            " MB)\nMemory that is not currently in use, and that will be repurposed first when processes, "
            + "drivers, or the operating system need more memory");
    }

    private string BuildInUseTooltip(MemoryPerformanceSnapshot memory)
    {
        string tooltip = string.Concat(
            str0: "In use (",
            FormatMebibytes(memory.UsedPhysicalBytes),
            str2: " MB)\nMemory used by processes, drivers, or the operating system");
        MemoryCompositionSnapshot composition = memory.Composition;
        if (!composition.HasCompressionData) return tooltip;

        return string.Concat(
            tooltip,
            "\n\nIn use compressed (",
            FormatMebibytes(composition.CompressedBytes),
            " MB)\nCompressed memory stores an estimated ",
            FormatMebibytes(composition.EstimatedDataBytes),
            " MB of data, saving the system ",
            FormatMebibytes(composition.SavedBytes),
            " MB of memory");
    }

    private TextBlock CreateTooltip() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = _resources.AxamlTaskManagerPerformance.MemoryCompositionTooltipMaximumWidth
    };

    private static Border CreateSegment(
        IBrush background,
        IBrush borderBrush,
        Thickness borderThickness) => new()
    {
        Background = background, BorderBrush = borderBrush, BorderThickness = borderThickness
    };

    private static GridLength Star(ulong value) =>
        new(value, GridUnitType.Star);

    private static string FormatMebibytes(ulong bytes)
    {
        double mebibytes = bytes / (double)BytesPerMebibyte;
        ulong roundedMebibytes = mebibytes >= ulong.MaxValue
            ? ulong.MaxValue
            : (ulong)Math.Round(mebibytes, MidpointRounding.AwayFromZero);
        return roundedMebibytes.ToString(format: "0", CultureInfo.CurrentCulture);
    }
}
