using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Models;
using TrayAppDotNETCommon.Utils;
using static TaskManagerTrayAppDotNET.UI.Performance.PerformanceText;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>
/// Owned, non-modal editor for one Performance device card: its title, its description and the replacements on
/// it, how many lines it shows and their formats, and its graph color. Every edit applies to the page at once.
/// </summary>
internal sealed class PerformanceDeviceCardWindow : Window, IDisposable
{
    private readonly SettingsPalette _palette;
    private readonly bool _enableRoundedCorners;
    private readonly TaskManagerWindowResources _resources;
    private readonly StackPanel _contentStack = new();
    private readonly Grid _titleBar;
    private readonly TrayAppDotNETCaptionCloseButton _closeButton;
    private readonly List<IDisposable> _ownedControls = [];
    private PerformanceDevicePresentation _device;
    private PerformanceDeviceCardCustomization _customization;
    private Action<PerformanceDeviceCardCustomization, bool>? _apply;
    private TrayAppDotNETColorPickerWindow? _colorPicker;
    private TextBox? _titleBox;
    private TextBox? _descriptionBox;
    private StackPanel? _ruleRows;
    private SettingsSwatch? _colorSwatch;
    private TextBlock? _valueNames;
    private int _disposed;

    public PerformanceDeviceCardWindow(
        PerformanceDevicePresentation device,
        PerformanceDeviceCardCustomization customization,
        SettingsPalette palette,
        bool enableRoundedCorners,
        Action<PerformanceDeviceCardCustomization, bool> apply)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(customization);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(apply);

        _device = device;
        _customization = customization;
        _palette = palette;
        _enableRoundedCorners = enableRoundedCorners;
        _apply = apply;
        _resources = TaskManagerWindowResources.Current;
        DeviceID = device.DeviceID;

        string title = F(nameof(AppStrings.PerfCard_WindowTitleFormat), device.Title);
        Title = title;
        double width = _resources.AxamlPerformanceCardWindow.WindowWidth;
        Width = width;
        MinWidth = width;
        MaxWidth = width;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        this.MapTo(ControlMap.PerformanceDeviceCard.ID);
        this.MapCommand(ControlMap.PerformanceDeviceCard.Dismiss, Dismiss);

        _closeButton = new TrayAppDotNETCaptionCloseButton(palette)
            .MapTo(ControlMap.PerformanceDeviceCard.TitleBar.Close);
        _closeButton.Click += OnCloseClick;
        TrayAppDotNETToolTip.SetTip(_closeButton, tip: L(nameof(CommonStrings.Common_Close)));
        TrayAppDotNETToolTip.SuppressWhileEngaged(_closeButton);
        _titleBar = BuildTitleBar(title).MapTo(ControlMap.PerformanceDeviceCard.TitleBar.ID);
        _titleBar.PointerPressed += OnTitleBarPointerPressed;

        Content = BuildRoot();
        BuildContent();
        Closed += OnClosed;
    }

    /// <summary>The stable ID of the device whose card the window edits.</summary>
    public string DeviceID { get; }

    /// <summary>Refreshes the defaults the window shows as placeholders from the device's latest sample.</summary>
    public void UpdateDevice(PerformanceDevicePresentation device)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        _device = device;
        if (_titleBox != null) _titleBox.PlaceholderText = device.Title;
        if (_descriptionBox != null) _descriptionBox.PlaceholderText = device.Description;
        if (_valueNames != null) _valueNames.Text = FormatValueNames(device);
    }

    private Border BuildRoot()
    {
        Border body = new() { Padding = _resources.AxamlPerformanceCardWindow.ContentPadding, Child = _contentStack };
        Grid chrome = new();
        chrome.RowDefinitions.Add(new RowDefinition(
            new GridLength(_resources.AxamlPerformanceCardWindow.TitleBarHeight)));
        chrome.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        chrome.Children.Add(_titleBar);
        Grid.SetRow(body, value: 1);
        chrome.Children.Add(body);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = _resources.AxamlPerformanceCardWindow.RootBorderThickness,
            CornerRadius = _enableRoundedCorners ? _resources.AxamlPerformanceCardWindow.RootCornerRadius : default,
            Child = chrome
        };
    }

    private Grid BuildTitleBar(string title)
    {
        Grid titleBar = new() { Background = Brushes.Transparent };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock titleText = TrayAppDotNETSettingsUI.Text(
            title,
            _palette,
            _resources.AxamlPerformanceCardWindow.TitleFontSize,
            (FontWeight)_resources.AxamlPerformanceCardWindow.TitleFontWeight);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = _resources.AxamlPerformanceCardWindow.TitleMargin;
        titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        titleBar.Children.Add(titleText);

        Grid.SetColumn(_closeButton, value: 1);
        titleBar.Children.Add(_closeButton);
        return titleBar;
    }

    /// <summary>Builds every section from the current customization; Reset card builds them again.</summary>
    private void BuildContent()
    {
        DisposeOwnedControls();
        _contentStack.Children.Clear();
        CornerRadius cardRadius = _enableRoundedCorners
            ? _resources.AxamlPerformanceCardWindow.CardCornerRadius
            : default;

        _titleBox = TrayAppDotNETSettingsUI.TextBox(
                _palette,
                _resources.AxamlPerformanceCardWindow.ControlWidth,
                _customization.Title)
            .MapTo(ControlMap.PerformanceDeviceCard.Title);
        _titleBox.PlaceholderText = _device.Title;
        _titleBox.TextChanged += (_, _) =>
        {
            _customization.Title = _titleBox.Text ?? string.Empty;
            Publish(save: true);
        };
        _contentStack.Children.Add(TrayAppDotNETSettingsCards.Card(
            L(nameof(AppStrings.PerfCard_Title_Title)),
            L(nameof(AppStrings.PerfCard_Title_Description)),
            _titleBox,
            _palette,
            cardRadius));

        _contentStack.Children.Add(BuildDescriptionCard(cardRadius));
        _contentStack.Children.Add(BuildLinesCard(cardRadius));
        _contentStack.Children.Add(BuildColorCard(cardRadius));

        SettingsButton resetButton = TrayAppDotNETSettingsUI.Button(L(nameof(AppStrings.PerfCard_ResetCard)), _palette)
            .MapTo(ControlMap.PerformanceDeviceCard.ResetCard);
        resetButton.HorizontalAlignment = HorizontalAlignment.Right;
        resetButton.Margin = _resources.AxamlPerformanceCardWindow.FooterMargin;
        resetButton.Click += (_, _) => ResetCard();
        _contentStack.Children.Add(resetButton);
    }

    private Border BuildDescriptionCard(CornerRadius cardRadius)
    {
        _descriptionBox = TrayAppDotNETSettingsUI.TextBox(_palette, double.NaN, _customization.Description)
            .MapTo(ControlMap.PerformanceDeviceCard.Description);
        _descriptionBox.PlaceholderText = _device.Description;
        _descriptionBox.Margin = _resources.AxamlPerformanceCardWindow.SectionContentMargin;
        _descriptionBox.TextChanged += (_, _) =>
        {
            _customization.Description = _descriptionBox.Text ?? string.Empty;
            Publish(save: true);
        };

        SettingsButton addButton = TrayAppDotNETSettingsUI.Button(
                L(nameof(AppStrings.Settings_Performance_HardwareNameReplacements_AddReplacement)),
                _palette)
            .MapTo(ControlMap.PerformanceDeviceCard.DescriptionReplacements.AddReplacement);
        addButton.HorizontalAlignment = HorizontalAlignment.Left;
        addButton.Margin = _resources.AxamlTaskManagerSettings.HardwareNameRulesActionMargin;
        addButton.Click += (_, _) =>
        {
            _customization.DescriptionReplacements.Add(new PerformanceTextReplacementRule());
            RebuildRuleRows();
            Publish(save: true);
        };

        _ruleRows = new StackPanel
        {
            Margin = _resources.AxamlTaskManagerSettings.HardwareNameRulesContentMargin,
            Spacing = _resources.AxamlTaskManagerSettings.HardwareNameRuleRowSpacing
        };
        RebuildRuleRows();

        StackPanel content = new()
        {
            Children =
            {
                TrayAppDotNETSettingsUI.TitleText(L(nameof(AppStrings.PerfCard_Description_Title)), _palette),
                // NOTE: never format this text; its {description}, $1 and ${name} are literal syntax
                TrayAppDotNETSettingsUI.DescriptionText(L(nameof(AppStrings.PerfCard_Description_Description)), _palette),
                _descriptionBox,
                addButton,
                _ruleRows
            }
        };
        return TrayAppDotNETSettingsCards.RawCard(content, _palette, cardRadius);
    }

    /// <summary>Shows one row per description replacement, in the order they run.</summary>
    private void RebuildRuleRows()
    {
        if (_ruleRows == null) return;

        _ruleRows.Children.Clear();
        List<PerformanceTextReplacementRule> rules = _customization.DescriptionReplacements;
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            PerformanceTextReplacementRule rule = rules[ruleIndex];
            int index = ruleIndex;
            _ruleRows.Children.Add(ReplacementRuleRow.Build(
                rule.MatchPattern,
                rule.Replacement,
                L(nameof(AppStrings.PerfCard_DescriptionReplacements_MatchPattern_Tooltip)),
                matchPattern =>
                {
                    rule.MatchPattern = matchPattern;
                    Publish(save: true);
                },
                replacement =>
                {
                    rule.Replacement = replacement;
                    Publish(save: true);
                },
                () =>
                {
                    if ((uint)index < (uint)_customization.DescriptionReplacements.Count)
                        _customization.DescriptionReplacements.RemoveAt(index);
                    RebuildRuleRows();
                    Publish(save: true);
                },
                new ReplacementRuleRowNodes(
                    ControlMap.PerformanceDeviceCard.DescriptionReplacements.Rule.ID,
                    ControlMap.PerformanceDeviceCard.DescriptionReplacements.Rule.MatchPattern,
                    ControlMap.PerformanceDeviceCard.DescriptionReplacements.Rule.Replacement,
                    ControlMap.PerformanceDeviceCard.DescriptionReplacements.Rule.Delete),
                _palette,
                _resources));
        }
    }

    private Border BuildLinesCard(CornerRadius cardRadius)
    {
        PerformanceDeviceKind kind = _device.Kind;
        SettingsComboBox lineCount = TrayAppDotNETSettingsUI.ComboBox(
                _palette,
                _resources.AxamlPerformanceCardWindow.ControlWidth)
            .MapTo(ControlMap.PerformanceDeviceCard.LineCount);
        lineCount.Items.Add(new SettingsComboBoxItem(0, L(nameof(AppStrings.PerfCard_LineCount_None)), _palette));
        lineCount.Items.Add(new SettingsComboBoxItem(1, L(nameof(AppStrings.PerfCard_LineCount_One)), _palette));
        lineCount.Items.Add(new SettingsComboBoxItem(2, L(nameof(AppStrings.PerfCard_LineCount_Two)), _palette));
        int effectiveLineCount = _customization.LineCount == PerformanceDeviceCardCustomization.DefaultLineCount
            ? PerformanceDeviceCardLayout.GetDefaultLineCount(kind)
            : _customization.LineCount;
        foreach (SettingsComboBoxItem item in lineCount.Items)
        {
            if (item.Tag is not int count || count != effectiveLineCount) continue;
            lineCount.SelectedItem = item;
            break;
        }

        lineCount.SelectionChanged += (_, _) =>
        {
            if (lineCount.SelectedItem?.Tag is not int count) return;

            // The kind's own count is stored as the default, so the card follows any later change to it
            _customization.LineCount = count == PerformanceDeviceCardLayout.GetDefaultLineCount(kind)
                ? PerformanceDeviceCardCustomization.DefaultLineCount
                : count;
            Publish(save: true);
        };
        _ownedControls.Add(lineCount);

        Grid formats = new()
        {
            Margin = _resources.AxamlPerformanceCardWindow.SectionContentMargin,
            ColumnSpacing = _resources.AxamlPerformanceCardWindow.FormatColumnSpacing,
            RowSpacing = _resources.AxamlPerformanceCardWindow.FormatRowSpacing,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
        };
        SearchColumnCatalog valueCatalog = CreateValueCatalog(_device);
        for (int lineIndex = 0; lineIndex < PerformanceDeviceCardCustomization.MaximumLineCount; lineIndex++)
        {
            formats.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            TextBlock label = TrayAppDotNETSettingsUI.DescriptionText(
                L(lineIndex == 0
                    ? nameof(AppStrings.PerfCard_FirstLine_Label)
                    : nameof(AppStrings.PerfCard_SecondLine_Label)),
                _palette,
                margin: new Thickness());
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(label, lineIndex);
            formats.Children.Add(label);

            string defaultFormat = PerformanceDeviceCardLayout.GetDefaultLineFormat(kind, lineIndex);
            string format = _customization.GetLineFormat(lineIndex);
            TextBox formatBox = TrayAppDotNETSettingsUI.TextBox(
                    _palette,
                    double.NaN,
                    format.Length > 0 ? format : defaultFormat)
                .MapTo(lineIndex == 0
                    ? ControlMap.PerformanceDeviceCard.FirstLineFormat
                    : ControlMap.PerformanceDeviceCard.SecondLineFormat);
            formatBox.PlaceholderText = defaultFormat;
            int index = lineIndex;
            formatBox.TextChanged += (_, _) =>
            {
                string text = formatBox.Text ?? string.Empty;
                // The default format is stored as empty, so the card follows any later change to it
                _customization.SetLineFormat(
                    index,
                    string.Equals(text, defaultFormat, StringComparison.Ordinal) ? string.Empty : text);
                Publish(save: true);
            };
            Grid.SetRow(formatBox, lineIndex);
            Grid.SetColumn(formatBox, value: 1);
            formats.Children.Add(formatBox);

            // Typing { offers the device's values, as the table search boxes offer their columns
            SearchAutocompleteController autocomplete = new(
                formatBox,
                valueCatalog,
                ControlMap.PerformanceDeviceCard.LineFormatSuggestion,
                _palette,
                _enableRoundedCorners);
            formats.Children.Add(autocomplete.Popup);
            _ownedControls.Add(autocomplete);
        }

        _valueNames = TrayAppDotNETSettingsUI.DescriptionText(FormatValueNames(_device), _palette);
        Grid header = new()
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        StackPanel headerText = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                TrayAppDotNETSettingsUI.TitleText(L(nameof(AppStrings.PerfCard_Lines_Title)), _palette),
                TrayAppDotNETSettingsUI.DescriptionText(L(nameof(AppStrings.PerfCard_Lines_Description)), _palette)
            }
        };
        header.Children.Add(headerText);
        lineCount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lineCount, value: 1);
        header.Children.Add(lineCount);

        StackPanel content = new()
        {
            Children =
            {
                header,
                formats,
                // NOTE: never format this text; its braces and brackets are literal format syntax
                TrayAppDotNETSettingsUI.DescriptionText(L(nameof(AppStrings.PerfCard_LineFormat_Help)), _palette),
                _valueNames
            }
        };
        return TrayAppDotNETSettingsCards.RawCard(content, _palette, cardRadius);
    }

    private Border BuildColorCard(CornerRadius cardRadius)
    {
        _colorSwatch = new SettingsSwatch(_palette).MapTo(ControlMap.PerformanceDeviceCard.GraphColor.PickColor);
        UpdateColorSwatch();
        TrayAppDotNETToolTip.SetTip(_colorSwatch, L(nameof(AppStrings.PerfCard_Color_Title)));
        _colorSwatch.Click += OnColorSwatchClick;

        SettingsButton resetButton = TrayAppDotNETSettingsUI.Button(L(nameof(CommonStrings.Settings_Theme_Reset)), _palette)
            .MapTo(ControlMap.PerformanceDeviceCard.GraphColor.Reset);
        resetButton.Click += (_, _) =>
        {
            _colorPicker?.Close();
            _customization.AccentColor = string.Empty;
            UpdateColorSwatch();
            Publish(save: true);
        };

        return TrayAppDotNETSettingsCards.Card(
            L(nameof(AppStrings.PerfCard_Color_Title)),
            L(nameof(AppStrings.PerfCard_Color_Description)),
            TrayAppDotNETSettingsUI.Horizontal(_colorSwatch, resetButton),
            _palette,
            cardRadius);
    }

    private Color DefaultAccent => PerformanceDeviceAccent.Resolve(_device.Kind, customAccent: null).Primary;

    private void UpdateColorSwatch() =>
        _colorSwatch?.SetColor(ColorMath.TryParseHexOrNull(_customization.AccentColor), DefaultAccent);

    /// <summary>Opens the shared color picker, or brings back the one already open; it previews as it changes.</summary>
    private void OnColorSwatchClick(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        if (_colorPicker != null)
        {
            if (_colorPicker.WindowState == WindowState.Minimized) _colorPicker.WindowState = WindowState.Normal;
            _colorPicker.Activate();
            return;
        }

        TrayAppDotNETColorPickerWindow picker = new(
            F(nameof(AppStrings.PerfCard_Color_PickerTitleFormat), ResolveCardTitle()),
            hasAlpha: false,
            ColorMath.TryParseHexOrNull(_customization.AccentColor) ?? DefaultAccent,
            DefaultAccent,
            _palette.Snapshot(),
            TrayAppDotNETColorPickerStrings.Localized(),
            _enableRoundedCorners) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        picker.ColorChanged += OnPickerColorChanged;
        picker.Closed += OnPickerClosed;
        _colorPicker = picker;
        try
        {
            picker.Show(this);
        }
        catch
        {
            _colorPicker = null;
            picker.ColorChanged -= OnPickerColorChanged;
            picker.Closed -= OnPickerClosed;
            throw;
        }
    }

    private void OnPickerColorChanged(object? sender, Color color)
    {
        SetAccentColor(color);
        Publish(save: false);
    }

    /// <summary>Saves the color the picker ended on; a picker that changed nothing saves nothing.</summary>
    private void OnPickerClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is not TrayAppDotNETColorPickerWindow picker) return;

        picker.ColorChanged -= OnPickerColorChanged;
        picker.Closed -= OnPickerClosed;
        if (ReferenceEquals(_colorPicker, picker)) _colorPicker = null;
        if (Volatile.Read(ref _disposed) != 0 || !picker.IsDirty) return;

        SetAccentColor(picker.CurrentColor);
        Publish(save: true);
    }

    /// <summary>Stores a color, or the default when it is the device kind's own color.</summary>
    private void SetAccentColor(Color color)
    {
        _customization.AccentColor = color == DefaultAccent ? string.Empty : NullableThemeColor.ToHex(color);
        UpdateColorSwatch();
    }

    private string ResolveCardTitle() =>
        _customization.Title.Length > 0 ? _customization.Title : _device.Title;

    private void ResetCard()
    {
        _colorPicker?.Close();
        _customization = new PerformanceDeviceCardCustomization { DeviceID = DeviceID };
        BuildContent();
        Publish(save: true);
    }

    /// <summary>The values a line format can name, as a catalog the search autocomplete completes from.</summary>
    private static SearchColumnCatalog CreateValueCatalog(PerformanceDevicePresentation device)
    {
        ReadOnlySpan<PerformanceCardValue> values = device.CardValues.Span;
        SearchColumn[] columns = new SearchColumn[values.Length + 1];
        columns[0] = new SearchColumn(ID: 0, PerformanceCardValueNames.Description, string.Empty, SearchValueKind.Text);
        for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
        {
            columns[valueIndex + 1] = new SearchColumn(
                ID: valueIndex + 1,
                values[valueIndex].Name,
                string.Empty,
                SearchValueKind.Text);
        }

        return new SearchColumnCatalog(columns, defaultColumnIDs: []);
    }

    /// <summary>Lists the values a line format can insert for this device, e.g. <c>{utilization}</c>.</summary>
    private static string FormatValueNames(PerformanceDevicePresentation device)
    {
        ReadOnlySpan<PerformanceCardValue> values = device.CardValues.Span;
        string[] names = new string[values.Length + 1];
        names[0] = string.Concat("{", PerformanceCardValueNames.Description, "}");
        for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
            names[valueIndex + 1] = string.Concat("{", values[valueIndex].Name, "}");
        return string.Concat(L(nameof(AppStrings.PerfCard_Values_Label)), " ", string.Join("  ", names));
    }

    private void Publish(bool save)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _apply?.Invoke(_customization.Clone(), save);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!eventArgs.GetCurrentPoint(_titleBar).Properties.IsLeftButtonPressed) return;

        BeginMoveDrag(eventArgs);
        eventArgs.Handled = true;
    }

    private void OnCloseClick(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) == 0) Close();
    }

    // Escape anywhere in the window, like Close; the control map dispatches it
    private void Dismiss()
    {
        if (Volatile.Read(ref _disposed) == 0) Close();
    }

    private void OnClosed(object? sender, EventArgs eventArgs) => Dispose();

    private void DisposeOwnedControls()
    {
        for (int controlIndex = _ownedControls.Count - 1; controlIndex >= 0; controlIndex--)
            _ownedControls[controlIndex].Dispose();
        _ownedControls.Clear();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

        Closed -= OnClosed;
        _colorPicker?.Close();
        _titleBar.PointerPressed -= OnTitleBarPointerPressed;
        _closeButton.Click -= OnCloseClick;
        if (_colorSwatch != null) _colorSwatch.Click -= OnColorSwatchClick;
        DisposeOwnedControls();
        Content = null;
        _apply = null;
    }
}
