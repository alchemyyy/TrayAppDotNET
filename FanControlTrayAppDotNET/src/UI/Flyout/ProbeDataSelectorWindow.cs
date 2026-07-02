using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace FanControlTrayAppDotNET.UI;

/// <summary>
/// Window for selecting and transforming probe data shown by a probe card.
/// </summary>
public sealed partial class ProbeDataSelectorWindow : Window
{
    private static readonly ProbeSelectorTab[] Tabs =
    [
        ProbeSelectorTab.Home,
        ProbeSelectorTab.Temperatures,
        ProbeSelectorTab.Power,
        ProbeSelectorTab.Load,
        ProbeSelectorTab.Clocks,
        ProbeSelectorTab.Voltages,
    ];

    private readonly ProbeCard _probeCard;
    private readonly AppSettings _settings;
    private readonly Action<ProbeCard> _changed;
    private readonly SettingsPalette _palette;
    private readonly HashSet<string> _expandedTransformKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TextBlock>> _valueTextByKey = new(StringComparer.OrdinalIgnoreCase);
    private SelectorLayout? _layout;
    private ProbeSelectorTab _selectedTab = ProbeSelectorTab.Home;

    /// <summary>
    /// Initializes the XAML designer constructor.
    /// </summary>
    public ProbeDataSelectorWindow()
    {
        _probeCard = null!;
        _settings = null!;
        _changed = static _ => { };
        _palette = default;

        InitializeComponent();
        InitializeComponentState();
    }

    /// <summary>
    /// Initializes a selector window for a probe card.
    /// </summary>
    public ProbeDataSelectorWindow(ProbeCard probeCard, AppSettings settings, Action<ProbeCard> changed)
    {
        _probeCard = probeCard;
        _settings = settings;
        _changed = changed;
        _palette = FanSettingsWindow.CreatePalette(
            AppServices.Theme,
            settings,
            AppTheme.ResolveEffectiveIsLightTheme(settings));

        InitializeComponent();
        InitializeComponentState();
        Title = $"Probe Data: {_probeCard.DisplayName}";
        AppServices.LHMService?.PollTickCompleted += OnPollTickCompleted;
        Closed += OnClosed;
        RebuildContent();
    }

    private void InitializeComponentState() => _layout = SelectorLayout.From(this);

    private SelectorLayout Layout =>
        _layout ?? throw new InvalidOperationException("Probe selector layout resources have not been loaded.");

    /// <summary>
    /// Rebuilds the selector chrome and active tab body.
    /// </summary>
    private void RebuildContent()
    {
        _valueTextByKey.Clear();

        Grid shell = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            Margin = Layout.ContentMargin,
        };
        shell.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.TabRowHeight)));
        shell.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        shell.Children.Add(BuildTabRow());

        ScrollViewer body = new()
        {
            Margin = Layout.BodyMargin,
            Content = BuildTabBody(),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(body, 1);
        shell.Children.Add(body);

        Content = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = _settings.EnableRoundedCorners ? Layout.RootCornerRadius : Layout.ZeroCornerRadius,
            Child = shell,
        };
    }

    /// <summary>
    /// Builds the single-row browser-style tab strip.
    /// </summary>
    private Grid BuildTabRow()
    {
        Grid row = new();
        for (int i = 0; i < Tabs.Length; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (int i = 0; i < Tabs.Length; i++)
        {
            Border tab = BuildTab(Tabs[i]);
            Grid.SetColumn(tab, i);
            row.Children.Add(tab);
        }

        return row;
    }

    /// <summary>
    /// Builds one tab button.
    /// </summary>
    private Border BuildTab(ProbeSelectorTab tab)
    {
        bool selected = _selectedTab == tab;
        TextBlock label = TrayAppDotNETSettingsUI.Text(TabLabel(tab), _palette, Layout.TabFontSize,
            selected ? FontWeight.SemiBold : FontWeight.Normal);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;

        Border border = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(selected ? _palette.CardBackground : _palette.ControlBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = _settings.EnableRoundedCorners ? Layout.TabCornerRadius : Layout.ZeroCornerRadius,
            Margin = Layout.TabMargin,
            Padding = Layout.TabPadding,
            MinHeight = Layout.TabMinHeight,
            Child = label,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        border.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
            _selectedTab = tab;
            RebuildContent();
            e.Handled = true;
        };
        return border;
    }

    /// <summary>
    /// Builds the active tab body.
    /// </summary>
    private Control BuildTabBody()
    {
        return _selectedTab switch
        {
            ProbeSelectorTab.Home => BuildHomeBody(),
            ProbeSelectorTab.Temperatures => BuildTypeBody(DataSourceTypeEnum.Temperature),
            ProbeSelectorTab.Power => BuildTypeBody(DataSourceTypeEnum.Power),
            ProbeSelectorTab.Load => BuildTypeBody(DataSourceTypeEnum.Load),
            ProbeSelectorTab.Clocks => BuildTypeBody(DataSourceTypeEnum.Clock),
            ProbeSelectorTab.Voltages => BuildTypeBody(DataSourceTypeEnum.Voltage),
            _ => BuildHomeBody(),
        };
    }

    /// <summary>
    /// Builds the selected-probes home tab.
    /// </summary>
    private Control BuildHomeBody()
    {
        WrapPanel grid = new();
        List<ProbeCardProbe> selectedProbes =
        [
            .. _probeCard.Probes
                .Where(static probe => !string.IsNullOrWhiteSpace(probe.DataSourceKey))
                .OrderBy(probe => ProbeSortLabel(DataSource.Find(probe.DataSourceKey), probe),
                    StringComparer.OrdinalIgnoreCase)
        ];
        if (selectedProbes.Count == 0)
            return EmptyText("No probes selected");

        foreach (ProbeCardProbe probe in selectedProbes)
        {
            DataSource? source = DataSource.Find(probe.DataSourceKey);
            grid.Children.Add(source == null ? BuildMissingProbeCard(probe) : BuildProbeChoiceCard(source));
        }

        return grid;
    }

    /// <summary>
    /// Builds a typed probe grid tab.
    /// </summary>
    private Control BuildTypeBody(DataSourceTypeEnum type)
    {
        WrapPanel grid = new();
        List<DataSource> sources =
        [
            .. DataSource.DataSources.Values
                .Where(source => source.DataSourceType == type && ProbeValueFormatter.IsProbeDataSource(source))
                .OrderBy(static source => source.ControllerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
        ];
        if (sources.Count == 0)
            return EmptyText("No probes found");

        foreach (DataSource source in sources)
            grid.Children.Add(BuildProbeChoiceCard(source));

        return grid;
    }

    /// <summary>
    /// Builds a card for a selectable live data source.
    /// </summary>
    private Border BuildProbeChoiceCard(DataSource source)
    {
        ProbeCardProbe? selectedProbe = _probeCard.FindProbe(source.DataSourceKey);
        bool isSelected = selectedProbe != null;
        bool isExpanded = isSelected && _expandedTransformKeys.Contains(source.DataSourceKey);

        Grid card = new();
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (isExpanded) card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock deviceName = TrayAppDotNETSettingsUI.Text(source.ControllerName, _palette, Layout.CardTitleFontSize,
            FontWeight.SemiBold);
        deviceName.TextTrimming = TextTrimming.CharacterEllipsis;
        deviceName.Margin = Layout.TextColumnMargin;
        Grid.SetColumnSpan(deviceName, 3);
        card.Children.Add(deviceName);

        TextBlock probeValue = TrayAppDotNETSettingsUI.Text(ProbeValueLine(source, selectedProbe),
            _palette, Layout.CardValueFontSize);
        probeValue.TextTrimming = TextTrimming.CharacterEllipsis;
        probeValue.Margin = Layout.ValueRowMargin;
        RegisterValueText(source.DataSourceKey, probeValue);
        Grid.SetRow(probeValue, 1);
        card.Children.Add(probeValue);

        SettingsToggle toggle = TrayAppDotNETSettingsUI.Toggle(_palette, isSelected,
            (_, enabled) => ToggleProbe(source, enabled));
        toggle.Margin = Layout.ToggleMargin;
        toggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(toggle, 1);
        Grid.SetColumn(toggle, 1);
        card.Children.Add(toggle);

        SettingsButton gear = BuildGearButton(isSelected);
        gear.Margin = Layout.ActionButtonMargin;
        gear.Click += (_, _) =>
        {
            ToggleTransform(source.DataSourceKey);
        };
        Grid.SetRow(gear, 1);
        Grid.SetColumn(gear, 2);
        card.Children.Add(gear);

        if (isExpanded && selectedProbe != null)
        {
            Grid transformRow = BuildTransformRow(selectedProbe);
            Grid.SetRow(transformRow, 2);
            Grid.SetColumnSpan(transformRow, 3);
            card.Children.Add(transformRow);
        }

        return WrapCard(card);
    }

    /// <summary>
    /// Builds a home-tab card for a selected source that is not currently live.
    /// </summary>
    private Border BuildMissingProbeCard(ProbeCardProbe probe)
    {
        Grid card = new();
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock title = TrayAppDotNETSettingsUI.Text(probe.DataSourceKey, _palette, Layout.CardTitleFontSize,
            FontWeight.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = Layout.TextColumnMargin;
        Grid.SetColumnSpan(title, 2);
        card.Children.Add(title);

        TextBlock value = TrayAppDotNETSettingsUI.Text("--", _palette, Layout.CardValueFontSize);
        value.Margin = Layout.ValueRowMargin;
        Grid.SetRow(value, 1);
        card.Children.Add(value);

        SettingsToggle toggle = TrayAppDotNETSettingsUI.Toggle(_palette, true,
            (_, enabled) =>
            {
                if (enabled) return;
                _probeCard.Probes.Remove(probe);
                _expandedTransformKeys.Remove(probe.DataSourceKey);
                _changed(_probeCard);
                RebuildContent();
            });
        toggle.Margin = Layout.ToggleMargin;
        Grid.SetRow(toggle, 1);
        Grid.SetColumn(toggle, 1);
        card.Children.Add(toggle);
        return WrapCard(card);
    }

    /// <summary>
    /// Builds the transform editor row for a selected probe.
    /// </summary>
    private Grid BuildTransformRow(ProbeCardProbe probe)
    {
        Grid row = new()
        {
            Margin = Layout.TransformRowMargin,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        TextBlock label = TrayAppDotNETSettingsUI.Text("Transform", _palette, Layout.TransformLabelFontSize);
        label.Margin = Layout.TransformLabelMargin;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);

        TextBox textBox = TransformTextBox(probe.TransformString);
        textBox.Tag = probe;
        textBox.KeyDown += TransformTextBoxKeyDown;
        textBox.LostFocus += TransformTextBoxLostFocus;
        Grid.SetColumn(textBox, 1);
        row.Children.Add(textBox);
        return row;
    }

    /// <summary>
    /// Builds the settings gear button for a selectable probe card.
    /// </summary>
    private SettingsButton BuildGearButton(bool enabled)
    {
        SettingsButton button = new(GlyphCatalog.SETTINGS, _palette, transparentBase: true)
        {
            Width = Layout.ActionButtonWidth,
            Height = Layout.ActionButtonHeight,
            MinHeight = Layout.ActionButtonHeight,
            Padding = Layout.ZeroThickness,
            IsEnabled = enabled,
        };
        button.Label.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        button.Label.FontSize = Layout.GearFontSize;
        return button;
    }

    /// <summary>
    /// Builds a transform text box.
    /// </summary>
    private TextBox TransformTextBox(string text)
    {
        TextBox textBox = new()
        {
            Width = Layout.TransformBoxWidth,
            Height = Layout.TransformBoxHeight,
            Text = text,
            PlaceholderText = "x",
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = Layout.CardValueFontSize,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            Foreground = TrayAppDotNETSettingsUI.Brush(_palette.Foreground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = Layout.ZeroThickness,
            Padding = Layout.TransformBoxPadding,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        TrayAppDotNETSettingsUI.ApplyTextBoxResources(
            textBox,
            _palette,
            TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            TrayAppDotNETSettingsUI.Brush(_palette.Hover),
            TrayAppDotNETSettingsUI.Brush(_palette.TextBoxFocused));
        return textBox;
    }

    /// <summary>
    /// Wraps a selector card in the common card chrome.
    /// </summary>
    private Border WrapCard(Control content) =>
        new()
        {
            Width = Layout.GridCardWidth,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CardBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = _settings.EnableRoundedCorners ? Layout.CardCornerRadius : Layout.ZeroCornerRadius,
            Padding = Layout.CardPadding,
            Margin = Layout.CardMargin,
            Child = content,
        };

    /// <summary>
    /// Builds empty-state text for a tab body.
    /// </summary>
    private TextBlock EmptyText(string text)
    {
        TextBlock block = TrayAppDotNETSettingsUI.Text(text, _palette, Layout.EmptyFontSize);
        block.Foreground = TrayAppDotNETSettingsUI.Brush(_palette.SecondaryForeground);
        block.HorizontalAlignment = HorizontalAlignment.Center;
        block.VerticalAlignment = VerticalAlignment.Center;
        return block;
    }

    /// <summary>
    /// Toggles whether the probe card displays a source.
    /// </summary>
    private void ToggleProbe(DataSource source, bool enabled)
    {
        ProbeCardProbe? probe = _probeCard.FindProbe(source.DataSourceKey);
        if (enabled)
        {
            if (probe == null)
            {
                _probeCard.Probes.Add(new ProbeCardProbe { DataSourceKey = source.DataSourceKey });
                _changed(_probeCard);
            }

            RebuildContent();
            return;
        }

        if (probe != null)
        {
            _probeCard.Probes.Remove(probe);
            _expandedTransformKeys.Remove(source.DataSourceKey);
            _changed(_probeCard);
        }

        RebuildContent();
    }

    /// <summary>
    /// Toggles the transform editor for a selected source.
    /// </summary>
    private void ToggleTransform(string dataSourceKey)
    {
        if (_probeCard.FindProbe(dataSourceKey) == null) return;
        if (!_expandedTransformKeys.Add(dataSourceKey))
            _expandedTransformKeys.Remove(dataSourceKey);
        RebuildContent();
    }

    /// <summary>
    /// Commits the transform expression on Enter.
    /// </summary>
    private void TransformTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (e.Key != Key.Enter) return;
        CommitTransformTextBox(textBox);
        e.Handled = true;
    }

    /// <summary>
    /// Commits the transform expression when focus leaves the editor.
    /// </summary>
    private void TransformTextBoxLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox textBox) CommitTransformTextBox(textBox);
    }

    /// <summary>
    /// Persists the transform expression from a text box.
    /// </summary>
    private void CommitTransformTextBox(TextBox textBox)
    {
        if (textBox.Tag is not ProbeCardProbe probe) return;
        string next = (textBox.Text ?? string.Empty).Trim();
        if (string.Equals(next, probe.TransformString, StringComparison.Ordinal)) return;
        probe.TransformString = next;
        _changed(_probeCard);
        RefreshVisibleValues();
    }

    /// <summary>
    /// Registers a value text block for live refresh.
    /// </summary>
    private void RegisterValueText(string dataSourceKey, TextBlock textBlock)
    {
        if (!_valueTextByKey.TryGetValue(dataSourceKey, out List<TextBlock>? list))
        {
            list = [];
            _valueTextByKey[dataSourceKey] = list;
        }

        list.Add(textBlock);
    }

    /// <summary>
    /// Refreshes the value text in visible selector cards.
    /// </summary>
    private void RefreshVisibleValues()
    {
        foreach ((string dataSourceKey, List<TextBlock> textBlocks) in _valueTextByKey)
        {
            DataSource? source = DataSource.Find(dataSourceKey);
            if (source == null) continue;
            ProbeCardProbe? probe = _probeCard.FindProbe(dataSourceKey);
            string value = ProbeValueLine(source, probe);
            foreach (TextBlock textBlock in textBlocks)
                textBlock.Text = value;
        }
    }

    /// <summary>
    /// Formats a probe name and current value for selector cards.
    /// </summary>
    private static string ProbeValueLine(DataSource source, ProbeCardProbe? probe) =>
        $"{source.DisplayName}: {ProbeValueFormatter.FormatValue(source, probe)}";

    /// <summary>
    /// Refreshes probe values after each LHM poll.
    /// </summary>
    private void OnPollTickCompleted()
    {
        if (!IsVisible) return;
        RefreshVisibleValues();
    }

    /// <summary>
    /// Unsubscribes selector events.
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        AppServices.LHMService?.PollTickCompleted -= OnPollTickCompleted;
        Closed -= OnClosed;
    }

    /// <summary>
    /// Resolves a stable sort label for a probe source.
    /// </summary>
    private static string ProbeSortLabel(DataSource? source, ProbeCardProbe probe) =>
        source == null ? probe.DataSourceKey : $"{source.ControllerName}.{source.DisplayName}";

    /// <summary>
    /// Resolves the tab label.
    /// </summary>
    private static string TabLabel(ProbeSelectorTab tab) => tab switch
    {
        ProbeSelectorTab.Home => "Home",
        ProbeSelectorTab.Temperatures => "Temperatures",
        ProbeSelectorTab.Power => "Power",
        ProbeSelectorTab.Load => "Load",
        ProbeSelectorTab.Clocks => "Clocks",
        ProbeSelectorTab.Voltages => "Voltages",
        _ => string.Empty,
    };

    private enum ProbeSelectorTab
    {
        Home,
        Temperatures,
        Power,
        Load,
        Clocks,
        Voltages,
    }

    private sealed record SelectorLayout(
        double TabRowHeight,
        double TabFontSize,
        double TabMinHeight,
        double CardTitleFontSize,
        double CardValueFontSize,
        double GridCardWidth,
        double EmptyFontSize,
        double ActionButtonWidth,
        double ActionButtonHeight,
        double GearFontSize,
        double TransformLabelFontSize,
        double TransformBoxWidth,
        double TransformBoxHeight,
        Thickness ZeroThickness,
        Thickness RootBorderThickness,
        Thickness ContentMargin,
        Thickness TabMargin,
        Thickness TabPadding,
        Thickness BodyMargin,
        Thickness CardMargin,
        Thickness CardPadding,
        Thickness TextColumnMargin,
        Thickness ValueRowMargin,
        Thickness ToggleMargin,
        Thickness ActionButtonMargin,
        Thickness TransformRowMargin,
        Thickness TransformLabelMargin,
        Thickness TransformBoxPadding,
        CornerRadius RootCornerRadius,
        CornerRadius CardCornerRadius,
        CornerRadius TabCornerRadius,
        CornerRadius ZeroCornerRadius)
    {
        /// <summary>
        /// Reads selector layout resources from XAML.
        /// </summary>
        public static SelectorLayout From(Control owner)
        {
            HotReloadResourceReader r = new(owner, "ProbeSelector");
            return new SelectorLayout(
                r.Double("TabRowHeight"),
                r.Double("TabFontSize"),
                r.Double("TabMinHeight"),
                r.Double("CardTitleFontSize"),
                r.Double("CardValueFontSize"),
                r.Double("GridCardWidth"),
                r.Double("EmptyFontSize"),
                r.Double("ActionButtonWidth"),
                r.Double("ActionButtonHeight"),
                r.Double("GearFontSize"),
                r.Double("TransformLabelFontSize"),
                r.Double("TransformBoxWidth"),
                r.Double("TransformBoxHeight"),
                r.Thickness("ZeroThickness"),
                r.Thickness("RootBorderThickness"),
                r.Thickness("ContentMargin"),
                r.Thickness("TabMargin"),
                r.Thickness("TabPadding"),
                r.Thickness("BodyMargin"),
                r.Thickness("CardMargin"),
                r.Thickness("CardPadding"),
                r.Thickness("TextColumnMargin"),
                r.Thickness("ValueRowMargin"),
                r.Thickness("ToggleMargin"),
                r.Thickness("ActionButtonMargin"),
                r.Thickness("TransformRowMargin"),
                r.Thickness("TransformLabelMargin"),
                r.Thickness("TransformBoxPadding"),
                r.CornerRadius("RootCornerRadius"),
                r.CornerRadius("CardCornerRadius"),
                r.CornerRadius("TabCornerRadius"),
                r.CornerRadius("ZeroCornerRadius"));
        }
    }
}
