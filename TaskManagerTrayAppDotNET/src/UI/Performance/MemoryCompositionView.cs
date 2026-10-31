using static TaskManagerTrayAppDotNET.UI.Performance.PerformanceText;
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
            text: L(nameof(AppStrings.Perf_Memory_Composition)),
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
        _modifiedTooltip.Text = F(nameof(AppStrings.Perf_Memory_ModifiedTooltipFormat), FormatMebibytes(modifiedBytes));
        _standbyTooltip.Text = F(
            composition.HasCompositionData
                ? nameof(AppStrings.Perf_Memory_StandbyTooltipFormat)
                : nameof(AppStrings.Perf_Memory_AvailableTooltipFormat),
            FormatMebibytes(standbyBytes));
        _freeTooltip.Text = F(nameof(AppStrings.Perf_Memory_FreeTooltipFormat), FormatMebibytes(freeBytes));
    }

    private static string BuildInUseTooltip(MemoryPerformanceSnapshot memory)
    {
        string tooltip = F(nameof(AppStrings.Perf_Memory_InUseTooltipFormat), FormatMebibytes(memory.UsedPhysicalBytes));
        MemoryCompositionSnapshot composition = memory.Composition;
        if (!composition.HasCompressionData) return tooltip;

        return string.Concat(
            tooltip,
            "\n\n",
            F(
                nameof(AppStrings.Perf_Memory_CompressedTooltipFormat),
                FormatMebibytes(composition.CompressedBytes),
                FormatMebibytes(composition.EstimatedDataBytes),
                FormatMebibytes(composition.SavedBytes)));
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
