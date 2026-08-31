using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.ComponentModel;
using TrayAppDotNETCommon.UI.Settings;
using TrayAppDotNETCommon.Visuals;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

public enum TaskManagerSettingsPage
{
    General,
    TrayIcon,
    Performance,
    Theme,
    About
}

/// <summary>Classic TrayAppDotNET settings window for Task Manager.</summary>
public sealed class TaskManagerSettingsWindow : SettingsWindowCommon<TaskManagerSettingsPage>
{
    private const int ToolTipDelayMinimumMilliseconds = 0;
    private const int ToolTipDelayMaximumMilliseconds = 10_000;

    private readonly AppSettings _settings;
    private readonly Action<string, InstallScope> _showUninstaller;
    private readonly TaskManagerWindowResources _taskManagerResources = TaskManagerWindowResources.Current;
    private SettingsButton? _resetPerformanceDeviceOrderButton;

    public TaskManagerSettingsWindow(
        AppSettings settings,
        Action<string, InstallScope> showUninstaller)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showUninstaller);

        _settings = settings;
        _showUninstaller = showUninstaller;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        ConfigureCompactSettingsWindow("Task Manager settings", icon: null);
        Topmost = settings.AlwaysOnTop;
        InitializeSettingsShell();
#if DEBUG
        TaskManagerWindowResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
#endif
    }

    internal new void SelectPage(TaskManagerSettingsPage page) => base.SelectPage(page);

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override bool UseWindows11SettingsNavigation => _settings.UseWindows11SettingsNavigation;

    protected override ISettingsSidebarWidthSettings SidebarWidthSettings => _settings;

    protected override TaskManagerSettingsPage DefaultPageKey => TaskManagerSettingsPage.General;

    protected override string HeaderText => "Task Manager";

    protected override string OpenSettingsFolderText => "Open Task Manager settings folder";

    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override Color ConfirmOverlayBackdrop =>
        (AppServices.Theme ?? AppTheme.Default).FlyoutOverlayBackdrop.For(ResolveEffectiveIsLight());

    protected override SettingsPalette ResolvePalette() =>
        VolumeSettingsPalette.Create(AppServices.Theme, _settings, ResolveEffectiveIsLight());

    protected override bool ResolveEffectiveIsLightForBindings() => ResolveEffectiveIsLight();

    protected override IReadOnlyList<SettingsPageDescriptor<TaskManagerSettingsPage>> CreatePageDescriptors() =>
    [
        new(
            TaskManagerSettingsPage.General,
            "General",
            () => NamePage(TaskManagerSettingsPage.General, BuildGeneralPage()),
            SettingsNavigationGlyphs.General),
        new(
            TaskManagerSettingsPage.TrayIcon,
            "Tray icon",
            () => NamePage(TaskManagerSettingsPage.TrayIcon, BuildTrayIconPage()),
            SettingsNavigationGlyphs.TrayIcon),
        new(
            TaskManagerSettingsPage.Performance,
            "Performance",
            () => NamePage(TaskManagerSettingsPage.Performance, BuildPerformancePage()),
            SettingsNavigationGlyphs.Devices),
        new(
            TaskManagerSettingsPage.Theme,
            "Appearance",
            () => NamePage(TaskManagerSettingsPage.Theme, BuildThemePage()),
            SettingsNavigationGlyphs.Theme),
        new(
            TaskManagerSettingsPage.About,
            "About",
            () => NamePage(TaskManagerSettingsPage.About, BuildAboutPage()),
            SettingsNavigationGlyphs.About)
    ];

    protected override void Save() => _settings.Save();

    protected override void OnSettingsWindowClosed()
    {
#if DEBUG
        TaskManagerWindowResources.ResourcesReloaded -= OnAXAMLResourcesReloaded;
#endif
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _resetPerformanceDeviceOrderButton = null;
        base.OnSettingsWindowClosed();
    }

#if DEBUG
    /// <summary>Rebuilds the open classic settings surface after Task Manager AXAML reloads.</summary>
    private void OnAXAMLResourcesReloaded()
    {
        if (!IsClosing) RebuildShell(CurrentPageKey);
    }
#endif

    private StackPanel BuildGeneralPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack("General", palette);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(palette);
        stack.Children.Add(commonSection.BuildStartupCard());
        stack.Children.Add(BoolCard(
            "Autosave settings",
            "Save changes to the Task Manager settings file as they are made.",
            _settings.Autosave,
            value => _settings.Autosave = value,
            palette,
            searchKeywords: ["save settings automatically"]));
        stack.Children.Add(BuildWindowManagementCard(palette));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Processes", palette));
        stack.Children.Add(BoolCard(
            "Skip Explorer restart confirmation",
            "Restart Windows Explorer immediately from the Processes page without asking for confirmation.",
            _settings.SkipRestartExplorerConfirmation,
            value => _settings.SkipRestartExplorerConfirmation = value,
            palette,
            searchKeywords: ["restart explorer confirmation prompt warning"]));

        commonSection.AddInstallationSection(
            stack,
            [
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.LocalAppData,
                    Title = "Install for current user",
                    ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                    Elevated = false,
                    Install = static () => AppServices.Installation.InstallToLocalAppData(),
                    UninstallAsync = _ =>
                    {
                        _showUninstaller(
                            AppServices.InstallLayout.LocalAppDataInstallDirectory,
                            InstallScope.LocalAppData);
                        return Task.CompletedTask;
                    }
                },
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.ProgramFiles,
                    Title = "Install system-wide",
                    ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                    Elevated = true,
                    Install = static () => AppServices.Installation.InstallSystemWide(),
                    UninstallAsync = _ =>
                    {
                        _showUninstaller(
                            AppServices.InstallLayout.ProgramFilesInstallDirectory,
                            InstallScope.ProgramFiles);
                        return Task.CompletedTask;
                    }
                }
            ]);

        CreateRenderingSettingsSection(palette).AddCards(stack);
        return stack;
    }

    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack("Tray icon", palette);
        stack.Children.Add(ComboCard(
            "Style",
            "Show only the latest value or a recency-weighted sliding history.",
            [
                (nameof(TrayGraphStyle.Current), "Current"),
                (nameof(TrayGraphStyle.Marquee), "Marquee")
            ],
            _settings.TrayGraphStyle.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayGraphStyle value))
                    _settings.TrayGraphStyle = value;
            },
            palette,
            searchKeywords: ["graph current marquee history sliding"]));
        stack.Children.Add(ComboCard(
            "Data source",
            "Choose the system utilization measured by the tray graph.",
            [
                (nameof(TrayGraphDataSource.CPUAverage), "CPU Usage (Average)"),
                (nameof(TrayGraphDataSource.CPUHighestCore), "CPU Usage (Highest Core)"),
                (nameof(TrayGraphDataSource.Memory), "Memory (RAM)")
            ],
            _settings.TrayGraphDataSource.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayGraphDataSource value))
                    _settings.TrayGraphDataSource = value;
            },
            palette,
            searchKeywords: ["CPU processor core memory RAM metric"]));
        return stack;
    }

    private StackPanel BuildPerformancePage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack("Performance", palette);
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("History and sampling", palette));
        stack.Children.Add(IntCard(
            "History length",
            "Keep each Performance graph's samples for this many minutes.",
            _settings.PerformanceHistoryLengthMinutes,
            PerformanceSamplingSettings.MinimumHistoryLengthMinutes,
            PerformanceSamplingSettings.MaximumHistoryLengthMinutes,
            value => _settings.PerformanceHistoryLengthMinutes = value,
            palette,
            " min",
            ["performance graph history retention minutes"]));
        stack.Children.Add(IntCard(
            "Sampling interval",
            "Wait this many milliseconds between Performance samples.",
            _settings.PerformanceSampleIntervalMilliseconds,
            PerformanceSamplingSettings.MinimumSampleIntervalMilliseconds,
            PerformanceSamplingSettings.MaximumSampleIntervalMilliseconds,
            value => _settings.PerformanceSampleIntervalMilliseconds = value,
            palette,
            " ms",
            ["performance refresh update rate frequency milliseconds"]));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Graphs", palette));
        stack.Children.Add(BoolCard(
            "Fill graph areas",
            "Draw a translucent shaded area beneath Performance graph lines.",
            _settings.ShowPerformanceGraphUnderfill,
            value => _settings.ShowPerformanceGraphUnderfill = value,
            palette,
            searchKeywords: ["performance graph underfill shade translucent area"]));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("CPU", palette));
        stack.Children.Add(BoolCard(
            "Show highest core trace",
            "Draw a thinner, dimmer highest-logical-processor trace behind the overall CPU graph.",
            _settings.ShowCPUHighestCoreTrace,
            value => _settings.ShowCPUHighestCoreTrace = value,
            palette,
            searchKeywords: ["CPU core logical processor utilization graph overlay"]));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Memory", palette));
        stack.Children.Add(BoolCard(
            "Show memory module serial numbers",
            "Display each physical memory module's serial number in the Memory performance details. "
            + "Serial numbers are hidden by default.",
            _settings.ShowMemoryModuleSerialNumbers,
            value => _settings.ShowMemoryModuleSerialNumbers = value,
            palette,
            searchKeywords: ["RAM DIMM privacy serial number"]));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Device column", palette));
        stack.Children.Add(BuildDevicePriorityCard(palette));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Hardware names", palette));
        stack.Children.Add(BuildHardwareNameReplacementCard(palette));
        return stack;
    }

    private Border BuildHardwareNameReplacementCard(SettingsPalette palette)
    {
        StackPanel content = new();
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText(
            "Hardware name replacements",
            palette));
        content.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            "Apply case-insensitive .NET regular expression replacements to device hardware names. "
            + "Rules run from top to bottom, and replacements support $1 and ${name} captures.",
            palette));

        SettingsButton addButton = Button("+ Add replacement", palette);
        addButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        addButton.Margin = _taskManagerResources.AxamlTaskManagerSettings
            .HardwareNameRulesActionMargin;
        addButton.Click += (_, _) => AddHardwareNameReplacementRule();
        content.Children.Add(addButton);

        StackPanel rows = new()
        {
            Margin = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRulesContentMargin,
            Spacing = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRuleRowSpacing
        };
        List<PerformanceHardwareNameReplacementRule> rules =
            _settings.PerformanceHardwareNameReplacementRules;
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            rows.Children.Add(BuildHardwareNameReplacementRow(
                ruleIndex,
                rules[ruleIndex],
                palette));
        }

        content.Children.Add(rows);
        return RawCard(
            content,
            palette,
            ["device hardware adapter rename regex match replace captures CPU GPU network disk"]);
    }

    private Border BuildHardwareNameReplacementRow(
        int ruleIndex,
        PerformanceHardwareNameReplacementRule rule,
        SettingsPalette palette)
    {
        SettingsComboBox deviceKind = TrayAppDotNETSettingsUI.ComboBox(
            palette,
            _taskManagerResources.AxamlTaskManagerSettings.HardwareNameRuleDeviceTypeWidth);
        foreach (PerformanceDeviceKind kind in Enum.GetValues<PerformanceDeviceKind>())
        {
            deviceKind.Items.Add(new SettingsComboBoxItem(
                kind,
                PerformanceDeviceLabel(kind),
                palette));
        }

        foreach (SettingsComboBoxItem item in deviceKind.Items)
        {
            if (item.Tag is not PerformanceDeviceKind kind || kind != rule.DeviceKind) continue;
            deviceKind.SelectedItem = item;
            break;
        }

        deviceKind.SelectionChanged += (_, _) =>
        {
            if (deviceKind.SelectedItem?.Tag is PerformanceDeviceKind kind)
                UpdateHardwareNameReplacementDeviceKind(ruleIndex, kind);
        };

        TextBox matchPattern = TrayAppDotNETSettingsUI.TextBox(
            palette,
            double.NaN,
            rule.MatchPattern);
        matchPattern.MinWidth = _taskManagerResources.AxamlTaskManagerSettings
            .HardwareNameRuleTextMinimumWidth;
        matchPattern.PlaceholderText = "Regex match";
        matchPattern.TextChanged += (_, _) => UpdateHardwareNameReplacementMatchPattern(
            ruleIndex,
            matchPattern.Text ?? string.Empty);
        TrayAppDotNETToolTip.SetTip(
            matchPattern,
            "Case-insensitive .NET regular expression matched against the hardware name.");

        TextBox replacement = TrayAppDotNETSettingsUI.TextBox(
            palette,
            double.NaN,
            rule.Replacement);
        replacement.MinWidth = _taskManagerResources.AxamlTaskManagerSettings
            .HardwareNameRuleTextMinimumWidth;
        replacement.PlaceholderText = "Replacement ($1)";
        replacement.TextChanged += (_, _) => UpdateHardwareNameReplacementValue(
            ruleIndex,
            replacement.Text ?? string.Empty);
        TrayAppDotNETToolTip.SetTip(
            replacement,
            "Replacement text. Use $1 or ${name} to insert a regex capture.");

        SettingsButton deleteButton = new(
            TaskManagerGlyphCatalog.CLOSE,
            palette,
            transparentBase: true)
        {
            Width = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRuleDeleteButtonSize,
            Height = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRuleDeleteButtonSize,
            MinHeight = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRuleDeleteButtonSize,
            Padding = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRuleDeleteButtonPadding
        };
        deleteButton.Click += (_, _) => DeleteHardwareNameReplacementRule(ruleIndex);
        TrayAppDotNETToolTip.SetTip(deleteButton, "Delete replacement");

        Grid row = new()
        {
            ColumnSpacing = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRuleColumnSpacing,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Children.Add(deviceKind);
        Grid.SetColumn(matchPattern, 1);
        row.Children.Add(matchPattern);
        Grid.SetColumn(replacement, 2);
        row.Children.Add(replacement);
        Grid.SetColumn(deleteButton, 3);
        row.Children.Add(deleteButton);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            CornerRadius = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRuleRowCornerRadius,
            Padding = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRuleRowPadding,
            Child = row
        };
    }

    private void AddHardwareNameReplacementRule()
    {
        List<PerformanceHardwareNameReplacementRule> rules =
            PerformanceHardwareNameReplacementRuleCollection.Normalize(
                _settings.PerformanceHardwareNameReplacementRules);
        rules.Add(new PerformanceHardwareNameReplacementRule());
        _settings.UpdatePerformanceHardwareNameReplacementRules(rules);
        RebuildShell(TaskManagerSettingsPage.Performance);
    }

    private void DeleteHardwareNameReplacementRule(int ruleIndex)
    {
        List<PerformanceHardwareNameReplacementRule> rules =
            PerformanceHardwareNameReplacementRuleCollection.Normalize(
                _settings.PerformanceHardwareNameReplacementRules);
        if ((uint)ruleIndex >= (uint)rules.Count) return;

        rules.RemoveAt(ruleIndex);
        _settings.UpdatePerformanceHardwareNameReplacementRules(rules);
        RebuildShell(TaskManagerSettingsPage.Performance);
    }

    private void UpdateHardwareNameReplacementDeviceKind(
        int ruleIndex,
        PerformanceDeviceKind deviceKind)
    {
        if (!Enum.IsDefined(deviceKind)) return;

        List<PerformanceHardwareNameReplacementRule> rules =
            PerformanceHardwareNameReplacementRuleCollection.Normalize(
                _settings.PerformanceHardwareNameReplacementRules);
        if ((uint)ruleIndex >= (uint)rules.Count || rules[ruleIndex].DeviceKind == deviceKind) return;

        rules[ruleIndex].DeviceKind = deviceKind;
        _settings.UpdatePerformanceHardwareNameReplacementRules(rules);
    }

    private void UpdateHardwareNameReplacementMatchPattern(int ruleIndex, string matchPattern)
    {
        List<PerformanceHardwareNameReplacementRule> rules =
            PerformanceHardwareNameReplacementRuleCollection.Normalize(
                _settings.PerformanceHardwareNameReplacementRules);
        if ((uint)ruleIndex >= (uint)rules.Count
            || string.Equals(rules[ruleIndex].MatchPattern, matchPattern, StringComparison.Ordinal))
        {
            return;
        }

        rules[ruleIndex].MatchPattern = matchPattern;
        _settings.UpdatePerformanceHardwareNameReplacementRules(rules);
    }

    private void UpdateHardwareNameReplacementValue(int ruleIndex, string replacement)
    {
        List<PerformanceHardwareNameReplacementRule> rules =
            PerformanceHardwareNameReplacementRuleCollection.Normalize(
                _settings.PerformanceHardwareNameReplacementRules);
        if ((uint)ruleIndex >= (uint)rules.Count
            || string.Equals(rules[ruleIndex].Replacement, replacement, StringComparison.Ordinal))
        {
            return;
        }

        rules[ruleIndex].Replacement = replacement;
        _settings.UpdatePerformanceHardwareNameReplacementRules(rules);
    }

    private Border BuildDevicePriorityCard(SettingsPalette palette)
    {
        List<PerformanceDeviceKind> priority =
            PerformanceDeviceOrdering.NormalizePriority(_settings.PerformanceDevicePriority);
        StackPanel rows = new()
        {
            Margin = _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityContentMargin,
            Spacing = _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityRowSpacing
        };
        for (int priorityIndex = 0; priorityIndex < priority.Count; priorityIndex++)
        {
            PerformanceDeviceKind kind = priority[priorityIndex];
            rows.Children.Add(BuildDevicePriorityRow(
                kind,
                priorityIndex,
                priority.Count,
                palette));
        }

        SettingsButton resetButton = TrayAppDotNETSettingsUI.Button("Reset default priority", palette);
        resetButton.IsEnabled = !priority.SequenceEqual(PerformanceDeviceOrdering.DefaultPriority);
        resetButton.Click += (_, _) => ResetPerformanceDevicePriority();
        SettingsButton resetDeviceOrderButton = TrayAppDotNETSettingsUI.Button(
            "Clear dragged device order",
            palette);
        _resetPerformanceDeviceOrderButton = resetDeviceOrderButton;
        resetDeviceOrderButton.IsEnabled = _settings.PerformanceDeviceOrder.Count > 0;
        resetDeviceOrderButton.Click += (_, _) => ResetPerformanceDeviceOrder();
        StackPanel resetActions = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityButtonSpacing,
            Children = { resetButton, resetDeviceOrderButton }
        };
        rows.Children.Add(resetActions);

        StackPanel content = new();
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText("Default device priority", palette));
        content.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            "Sets the category order for newly detected devices and devices that have not been reordered on the Performance page.",
            palette));
        content.Children.Add(rows);
        return RawCard(
            content,
            palette,
            ["CPU memory GPU network disk order new device"]);
    }

    private Border BuildDevicePriorityRow(
        PerformanceDeviceKind kind,
        int priorityIndex,
        int priorityCount,
        SettingsPalette palette)
    {
        TextBlock rank = TrayAppDotNETSettingsUI.Text(
            (priorityIndex + 1).ToString(),
            palette,
            _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityFontSize,
            (FontWeight)_taskManagerResources.AxamlTaskManagerSettings.DevicePriorityRankFontWeight);
        rank.Width = _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityRankWidth;
        rank.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

        TextBlock label = TrayAppDotNETSettingsUI.Text(
            PerformanceDeviceLabel(kind),
            palette,
            _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityFontSize,
            (FontWeight)_taskManagerResources.AxamlTaskManagerSettings.DevicePriorityLabelFontWeight);
        label.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

        SettingsButton moveUp = TrayAppDotNETSettingsUI.Button("Move up", palette);
        moveUp.IsEnabled = priorityIndex > 0;
        moveUp.Click += (_, _) => MovePerformanceDevicePriority(kind, -1);
        SettingsButton moveDown = TrayAppDotNETSettingsUI.Button("Move down", palette);
        moveDown.IsEnabled = priorityIndex + 1 < priorityCount;
        moveDown.Click += (_, _) => MovePerformanceDevicePriority(kind, 1);
        StackPanel actions = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityButtonSpacing,
            Children = { moveUp, moveDown }
        };

        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Children.Add(rank);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            CornerRadius = _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityRowCornerRadius,
            Padding = _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityRowPadding,
            Child = row
        };
    }

    private void MovePerformanceDevicePriority(PerformanceDeviceKind kind, int offset)
    {
        List<PerformanceDeviceKind> priority =
            PerformanceDeviceOrdering.NormalizePriority(_settings.PerformanceDevicePriority);
        int sourceIndex = priority.IndexOf(kind);
        int targetIndex = sourceIndex + offset;
        if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= priority.Count) return;

        priority.RemoveAt(sourceIndex);
        priority.Insert(targetIndex, kind);
        _settings.PerformanceDevicePriority = priority;
        Save();
        RebuildShell(TaskManagerSettingsPage.Performance);
    }

    private void ResetPerformanceDevicePriority()
    {
        _settings.PerformanceDevicePriority = PerformanceDeviceOrdering.CreateDefaultPriority();
        Save();
        RebuildShell(TaskManagerSettingsPage.Performance);
    }

    private void ResetPerformanceDeviceOrder()
    {
        _settings.PerformanceDeviceOrder = [];
        Save();
        RebuildShell(TaskManagerSettingsPage.Performance);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(AppSettings.PerformanceDeviceOrder)
            || _resetPerformanceDeviceOrderButton == null)
        {
            return;
        }

        _resetPerformanceDeviceOrderButton.IsEnabled = _settings.PerformanceDeviceOrder.Count > 0;
    }

    private static string PerformanceDeviceLabel(PerformanceDeviceKind kind) => kind switch
    {
        PerformanceDeviceKind.CPU => "CPU",
        PerformanceDeviceKind.Memory => "Memory",
        PerformanceDeviceKind.GPU => "GPU",
        PerformanceDeviceKind.Network => "Network",
        PerformanceDeviceKind.Disk => "Disk",
        _ => kind.ToString()
    };

    private Border BuildWindowManagementCard(SettingsPalette palette)
    {
        StackPanel options = new()
        {
            Margin = _taskManagerResources.AxamlTaskManagerSettings.WindowManagementOptionsMargin,
            Spacing = _taskManagerResources.AxamlTaskManagerSettings.WindowManagementOptionSpacing
        };
        options.Children.Add(CreateWindowManagementCheckBox(
            "Always on top",
            _settings.AlwaysOnTop,
            value =>
            {
                _settings.AlwaysOnTop = value;
                Topmost = value;
            },
            palette));
        options.Children.Add(CreateWindowManagementCheckBox(
            "Close to Tray",
            _settings.CloseToTray,
            value => _settings.CloseToTray = value,
            palette));
        options.Children.Add(CreateWindowManagementCheckBox(
            "Minimize to Tray",
            _settings.MinimizeToTray,
            value => _settings.MinimizeToTray = value,
            palette));

        StackPanel content = new();
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText("Window management", palette));
        content.Children.Add(options);
        return RawCard(
            content,
            palette,
            ["always on top", "close to tray", "minimize to tray"]);
    }

    private CheckBox CreateWindowManagementCheckBox(
        string text,
        bool isChecked,
        Action<bool> set,
        SettingsPalette palette)
    {
        CheckBox checkBox = new()
        {
            Content = TrayAppDotNETSettingsUI.Text(text, palette),
            IsChecked = isChecked,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground)
        };
        checkBox.IsCheckedChanged += (_, _) =>
        {
            set(checkBox.IsChecked == true);
            Save();
        };
        return checkBox;
    }

    private StackPanel BuildThemePage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack("Appearance", palette);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Processes grid", palette));
        stack.Children.Add(DoubleCard(
            "Font size",
            "Set the text size used by process rows.",
            _settings.GridFontSize,
            AppSettings.GridFontSizeMinimum,
            AppSettings.GridFontSizeMaximum,
            value => _settings.GridFontSize = value,
            palette,
            " DIP",
            ["grid text size", "zoom"],
            decimalPlaces: 1,
            step: 0.5));
        stack.Children.Add(ComboCard(
            "Font weight",
            "Set the text weight used by process rows and column headers.",
            [
                (nameof(DetailsGridFontWeight.Thin), "Thin"),
                (nameof(DetailsGridFontWeight.ExtraLight), "Extra light"),
                (nameof(DetailsGridFontWeight.Light), "Light"),
                (nameof(DetailsGridFontWeight.SemiLight), "Semi-light"),
                (nameof(DetailsGridFontWeight.Normal), "Normal"),
                (nameof(DetailsGridFontWeight.Medium), "Medium"),
                (nameof(DetailsGridFontWeight.SemiBold), "Semi-bold"),
                (nameof(DetailsGridFontWeight.Bold), "Bold"),
                (nameof(DetailsGridFontWeight.ExtraBold), "Extra bold"),
                (nameof(DetailsGridFontWeight.Black), "Black")
            ],
            _settings.GridFontWeight.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out DetailsGridFontWeight value))
                    _settings.GridFontWeight = value;
            },
            palette,
            searchKeywords: ["grid text thickness", "bold"]));
        stack.Children.Add(DoubleCard(
            "Row spacing",
            "Set the visible vertical gap between process rows.",
            _settings.GridRowSpacing,
            AppSettings.GridRowSpacingMinimum,
            AppSettings.GridRowSpacingMaximum,
            value => _settings.GridRowSpacing = value,
            palette,
            " DIP",
            ["grid height", "zoom"],
            decimalPlaces: 1,
            step: 0.5));
        stack.Children.Add(BoolCard(
            "Live column resizing",
            "Resize column contents while dragging instead of applying the new width on release.",
            _settings.EnableLiveDetailsColumnResizing,
            value => _settings.EnableLiveDetailsColumnResizing = value,
            palette,
            searchKeywords: ["column resize preview"]));
        stack.Children.Add(BoolCard(
            "Left-align search bar",
            "Align the Processes search bar with the left edge of the page area instead of centering it in the window.",
            _settings.LeftAlignProcessSearchBar,
            value => _settings.LeftAlignProcessSearchBar = value,
            palette,
            searchKeywords: ["process search position", "search alignment"]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Theme", palette));
        stack.Children.Add(ComboCard(
            "Theme mode",
            "Choose whether Task Manager follows Windows or uses a fixed light or dark theme.",
            [
                (nameof(TrayAppDotNETThemeMode.System), "System"),
                (nameof(TrayAppDotNETThemeMode.Light), "Light"),
                (nameof(TrayAppDotNETThemeMode.Dark), "Dark")
            ],
            _settings.ThemeMode.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayAppDotNETThemeMode value))
                    _settings.ThemeMode = value;
            },
            palette,
            afterSave: () => RebuildShell(TaskManagerSettingsPage.Theme),
            searchKeywords: ["light dark system"]));
        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Title)),
            L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Description)),
            _settings.UseWindows11SettingsNavigation,
            value => _settings.UseWindows11SettingsNavigation = value,
            palette,
            afterSave: () => RebuildShell(TaskManagerSettingsPage.Theme),
            searchKeywords: [L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_SearchKeywords))]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Window", palette));
        stack.Children.Add(BoolCard(
            "Rounded corners",
            "Use rounded corners on Task Manager and its menus.",
            _settings.EnableRoundedCorners,
            value => _settings.EnableRoundedCorners = value,
            palette,
            afterSave: () => RebuildShell(TaskManagerSettingsPage.Theme),
            searchKeywords: ["square sharp corners"]));
        stack.Children.Add(BoolCard(
            "Collapse navigation when narrow",
            "Hide the left navigation menu when the Task Manager window is narrower than 750 pixels.",
            _settings.CollapseSidebarWhenNarrow,
            value => _settings.CollapseSidebarWhenNarrow = value,
            palette,
            searchKeywords: ["sidebar left menu responsive"]));
        stack.Children.Add(ComboCard(
            "Animations",
            "Choose whether interface animations follow Windows, remain disabled, or remain enabled.",
            [
                (nameof(TrayAppDotNETAnimationMode.System), "System"),
                (nameof(TrayAppDotNETAnimationMode.Disabled), "Disabled"),
                (nameof(TrayAppDotNETAnimationMode.Enabled), "Enabled")
            ],
            _settings.AnimationMode.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayAppDotNETAnimationMode value))
                    _settings.AnimationMode = value;
            },
            palette,
            afterSave: ApplyAnimationMode,
            searchKeywords: ["motion transitions"]));

        stack.Children.Add(IntCard(
            "Tooltip delay",
            "Set how long the pointer must hover before a tooltip appears.",
            _settings.ToolTipShowDelayMs,
            ToolTipDelayMinimumMilliseconds,
            ToolTipDelayMaximumMilliseconds,
            value =>
            {
                _settings.ToolTipShowDelayMs = value;
                TrayAppDotNETToolTip.ShowDelayMs = value;
                TrayAppDotNETToolTip.ApplyShowDelayToSubtree(this);
            },
            palette,
            " ms",
            ["hover tooltip timing"]));

        return stack;
    }

    private StackPanel BuildAboutPage()
    {
        TrayAppDotNETAboutPage aboutPage = OwnPageResource(new TrayAppDotNETAboutPage(
            new TrayAppDotNETAboutPageOptions
            {
                Palette = Palette,
                ButtonRadius = RadiusMedium,
                CardRadius = RadiusLarge,
                UpdatePromptOwnerBackdrop = ConfirmOverlayBackdrop,
                L = L,
                Save = Save,
                ApplicationName = Constants.DisplayName,
                Tagline = "Fast process monitoring and management for TrayAppDotNET.",
                BuildNumber = BuildInfo.BuildNumber,
                CommitHash = BuildInfo.CommitHash,
                Publisher = Constants.Publisher,
                HelpLink = Constants.HelpLink,
                OpenSettingsFolderText = OpenSettingsFolderText,
                SettingsFolderPath = SettingsFolderPath,
                ConfirmAsync = ConfirmAsync,
                PromptOwner = () => this,
                Log = TADNLog.Log,
                SupportsFlyoutUpdateButton = false
            }));
        return aboutPage.Build();
    }

    private TrayAppDotNETGeneralSettingsSection CreateGeneralSettingsSection(SettingsPalette palette) =>
        new(new TrayAppDotNETGeneralSettingsSectionOptions
        {
            Palette = palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            L = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            GetRunOnStartup = static () => AppServices.Startup.GetRunOnStartup(),
            SetRunOnStartup = enabled =>
            {
                AppServices.Startup.SetRunOnStartup(enabled);
                _settings.RunOnStartup = enabled;
            },
            GetCurrentStartupShortcutTarget = static () => AppServices.Startup.GetCurrentShortcutTarget(),
            RetargetStartupShortcut = static () => AppServices.Startup.RetargetShortcutIfPresent(),
            DetectInstallations = static () => AppServices.Installation.DetectAll(),
            CurrentBuildNumber = BuildInfo.BuildNumber
        });

    private TrayAppDotNETRenderingSettingsSection CreateRenderingSettingsSection(SettingsPalette palette) =>
        new(new TrayAppDotNETRenderingSettingsSectionOptions
        {
            Palette = palette,
            CardRadius = RadiusLarge,
            L = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            RenderingSettings = _settings,
            TrayMenuSettings = _settings
        });

    private Control NamePage(TaskManagerSettingsPage page, Control control)
    {
        ControlNames.AssignLogicalSubtree(control, page.ToString());
        return control;
    }

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        TrayAppDotNETThemeMode.Light => true,
        TrayAppDotNETThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme
    };

    private void ApplyAnimationMode()
    {
        if (Application.Current != null)
            TrayAppDotNETAnimationPolicy.Apply(Application.Current, _settings.AnimationMode);
        RebuildShell(TaskManagerSettingsPage.Theme);
    }

}
