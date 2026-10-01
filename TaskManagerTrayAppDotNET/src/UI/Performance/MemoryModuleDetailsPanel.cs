using Avalonia.Controls;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI.Performance;

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
            text: "Physical memory layout",
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
            return;

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
            string.Concat(str0: "Bank: ", bankLabel),
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
                    labelText: "Capacity",
                    module.CapacityBytes > 0
                        ? PerformanceDevicePresentationFactory.FormatBytes(module.CapacityBytes)
                        : "Unavailable"),
                BuildModuleRow(
                    labelText: "Part number",
                    string.IsNullOrWhiteSpace(module.PartNumber)
                        ? "Unavailable"
                        : module.PartNumber)
            }
        };
        if (showSerialNumbers)
        {
            content.Children.Add(BuildModuleRow(
                labelText: "Serial number",
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
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            Children = { label, value }
        };
        Grid.SetColumn(value, value: 1);
        return row;
    }
}
