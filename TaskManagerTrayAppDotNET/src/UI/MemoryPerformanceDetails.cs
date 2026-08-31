using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Displays the in-use, modified, standby, and free physical-memory composition.</summary>
internal sealed class MemoryCompositionView : StackPanel
{
    private const ulong BytesPerMebibyte = 1_048_576;

    private readonly TaskManagerWindowResources _resources;
    private readonly ColumnDefinition _inUseColumn = new();
    private readonly ColumnDefinition _modifiedColumn = new();
    private readonly ColumnDefinition _standbyColumn = new();
    private readonly ColumnDefinition _freeColumn = new();
    private readonly Border _inUseSegment;
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
            "Memory composition",
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
        _inUseSegment = CreateSegment(
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
            default);
        _inUseTooltip = CreateTooltip();
        _modifiedTooltip = CreateTooltip();
        _standbyTooltip = CreateTooltip();
        _freeTooltip = CreateTooltip();
        TrayAppDotNETToolTip.SetTip(_inUseSegment, _inUseTooltip);
        TrayAppDotNETToolTip.SetTip(_modifiedSegment, _modifiedTooltip);
        TrayAppDotNETToolTip.SetTip(_standbySegment, _standbyTooltip);
        TrayAppDotNETToolTip.SetTip(_freeSegment, _freeTooltip);

        Grid segments = new()
        {
            ColumnDefinitions =
            {
                _inUseColumn,
                _modifiedColumn,
                _standbyColumn,
                _freeColumn
            },
            Children =
            {
                _inUseSegment,
                _modifiedSegment,
                _standbySegment,
                _freeSegment
            }
        };
        Grid.SetColumn(_modifiedSegment, 1);
        Grid.SetColumn(_standbySegment, 2);
        Grid.SetColumn(_freeSegment, 3);

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
            "Modified (",
            FormatMebibytes(modifiedBytes),
            " MB)\nMemory whose contents must be written to disk before it can be used for another purpose");
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
            "Free (",
            FormatMebibytes(freeBytes),
            " MB)\nMemory that is not currently in use, and that will be repurposed first when processes, "
            + "drivers, or the operating system need more memory");
    }

    private string BuildInUseTooltip(MemoryPerformanceSnapshot memory)
    {
        string tooltip = string.Concat(
            "In use (",
            FormatMebibytes(memory.UsedPhysicalBytes),
            " MB)\nMemory used by processes, drivers, or the operating system");
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
        Background = background,
        BorderBrush = borderBrush,
        BorderThickness = borderThickness
    };

    private static GridLength Star(ulong value) =>
        new(value, GridUnitType.Star);

    private static string FormatMebibytes(ulong bytes)
    {
        double mebibytes = bytes / (double)BytesPerMebibyte;
        ulong roundedMebibytes = mebibytes >= ulong.MaxValue
            ? ulong.MaxValue
            : (ulong)Math.Round(mebibytes, MidpointRounding.AwayFromZero);
        return roundedMebibytes.ToString("0", CultureInfo.CurrentCulture);
    }
}

/// <summary>Displays one compact, grouped card for each installed physical-memory module.</summary>
internal sealed class MemoryModuleDetailsPanel : StackPanel
{
    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly WrapPanel _moduleCards = new();
    private ReadOnlyMemory<PhysicalMemoryModuleSnapshot> _displayedModules;
    private bool _displayedSerialNumbers;

    public MemoryModuleDetailsPanel(
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        _palette = palette;
        _resources = resources;
        IsVisible = false;
        Margin = resources.AxamlTaskManagerPerformance.MemoryModulesMargin;

        TextBlock heading = TrayAppDotNETSettingsUI.Text(
            "Physical memory layout",
            palette,
            resources.AxamlTaskManagerPerformance.MemoryModuleHeadingFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        heading.Margin = resources.AxamlTaskManagerPerformance.MemoryModuleHeadingMargin;
        Children.Add(heading);

        ScrollViewer moduleScroll = new()
        {
            MaxHeight = resources.AxamlTaskManagerPerformance.MemoryModuleScrollMaximumHeight,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _moduleCards
        };
        Children.Add(moduleScroll);
    }

    /// <summary>Reconciles module cards when WMI metadata or the serial privacy setting changes.</summary>
    public void Update(
        ReadOnlyMemory<PhysicalMemoryModuleSnapshot> modules,
        bool showSerialNumbers)
    {
        IsVisible = modules.Length > 0;
        if (_displayedModules.Equals(modules)
            && _displayedSerialNumbers == showSerialNumbers)
        {
            return;
        }

        _displayedModules = modules;
        _displayedSerialNumbers = showSerialNumbers;
        _moduleCards.Children.Clear();
        ReadOnlySpan<PhysicalMemoryModuleSnapshot> moduleSpan = modules.Span;
        for (int moduleIndex = 0; moduleIndex < moduleSpan.Length; moduleIndex++)
            _moduleCards.Children.Add(BuildModuleCard(moduleSpan[moduleIndex], showSerialNumbers));
    }

    private Border BuildModuleCard(
        PhysicalMemoryModuleSnapshot module,
        bool showSerialNumbers)
    {
        string bankLabel = string.IsNullOrWhiteSpace(module.BankLabel)
            ? "Unavailable"
            : module.BankLabel;
        TextBlock bank = TrayAppDotNETSettingsUI.Text(
            string.Concat("Bank: ", bankLabel),
            _palette,
            _resources.AxamlTaskManagerPerformance.MemoryModuleBankFontSize,
            (FontWeight)_resources.AxamlTaskManagerPerformance.TextFontWeight);
        bank.Margin = _resources.AxamlTaskManagerPerformance.MemoryModuleBankMargin;
        bank.TextWrapping = TextWrapping.Wrap;

        StackPanel content = new()
        {
            Children =
            {
                bank,
                BuildModuleRow(
                    "Capacity",
                    module.CapacityBytes > 0
                        ? PerformanceDevicePresentationFactory.FormatBytes(module.CapacityBytes)
                        : "Unavailable"),
                BuildModuleRow(
                    "Part number",
                    string.IsNullOrWhiteSpace(module.PartNumber)
                        ? "Unavailable"
                        : module.PartNumber)
            }
        };
        if (showSerialNumbers)
        {
            content.Children.Add(BuildModuleRow(
                "Serial number",
                string.IsNullOrWhiteSpace(module.SerialNumber)
                    ? "Unavailable"
                    : module.SerialNumber));
        }

        return new Border
        {
            Width = _resources.AxamlTaskManagerPerformance.MemoryModuleCardWidth,
            Margin = _resources.AxamlTaskManagerPerformance.MemoryModuleCardMargin,
            Padding = _resources.AxamlTaskManagerPerformance.MemoryModuleCardPadding,
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = _resources.AxamlTaskManagerPerformance.MemoryModuleCardBorderThickness,
            CornerRadius = _resources.AxamlTaskManagerPerformance.MemoryModuleCardCornerRadius,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CardBackground),
            Child = content
        };
    }

    private Grid BuildModuleRow(string labelText, string valueText)
    {
        TextBlock label = TrayAppDotNETSettingsUI.Text(
            labelText,
            _palette,
            _resources.AxamlTaskManagerPerformance.MemoryModuleLabelFontSize,
            (FontWeight)_resources.AxamlTaskManagerPerformance.TextFontWeight);
        label.Width = _resources.AxamlTaskManagerPerformance.MemoryModuleLabelWidth;
        TextBlock value = TrayAppDotNETSettingsUI.Text(
            valueText,
            _palette,
            _resources.AxamlTaskManagerPerformance.MemoryModuleValueFontSize,
            (FontWeight)_resources.AxamlTaskManagerPerformance.TextFontWeight);
        value.TextWrapping = TextWrapping.Wrap;

        Grid row = new()
        {
            Margin = _resources.AxamlTaskManagerPerformance.MemoryModuleRowMargin,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            Children = { label, value }
        };
        Grid.SetColumn(value, 1);
        return row;
    }
}
