using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using TrayAppDotNETCommon.Visuals;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

public enum TaskManagerSettingsPage
{
    General,
    Processes,
    TrayIcon,
    Performance,
    Hotkeys,
    Theme,
    About
}

/// <summary>Classic TrayAppDotNET settings window for Task Manager.</summary>
public sealed class TaskManagerSettingsWindow : SettingsWindowCommon<TaskManagerSettingsPage>
{
    private const int ToolTipDelayMinimumMilliseconds = 0;
    private const int ToolTipDelayMaximumMilliseconds = 10_000;
    private const string ResetText = "Reset";
    private const string DIPSuffix = " DIP";
    private const string PercentSuffix = " %";
    private const double PercentScale = 100;
    private const double GridTypographyStep = 0.5;
    private const double LiveTotalHorizontalScaleStepPercent = 5;
    private const string ShortcutGestureSeparator = "\n";

    private readonly AppSettings _settings;
    private readonly Action<string, InstallScope, IProgress<TrayAppDotNETInstallProgress>?> _showUninstaller;
    private readonly TaskManagerWindowResources _taskManagerResources = TaskManagerWindowResources.Current;
    private readonly List<SettingsResettableNumber> _resettableNumbers = [];
    private SettingsButton? _resetPerformanceDeviceOrderButton;

    public TaskManagerSettingsWindow(
        AppSettings settings,
        Action<string, InstallScope, IProgress<TrayAppDotNETInstallProgress>?> showUninstaller)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showUninstaller);

        _settings = settings;
        _showUninstaller = showUninstaller;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        ConfigureCompactSettingsWindow(title: "Task Manager settings", icon: null);
        Topmost = settings.AlwaysOnTop;

        // A SettingsShell instance like the main window; tagged before the shell builds so its controls resolve
        this.MapTo(ControlMap.Settings.ID);
        InitializeSettingsShell();
#if DEBUG
        TaskManagerWindowResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
#endif
    }

    internal new void SelectPage(TaskManagerSettingsPage page) => base.SelectPage(page);

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override bool UseWindows11SettingsNavigation => _settings.UseWindows11SettingsNavigation;

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
            Label: "General",
            () => NamePage(TaskManagerSettingsPage.General, BuildGeneralPage()),
            SettingsNavigationGlyphs.General),
        new(
            TaskManagerSettingsPage.Processes,
            Label: "Processes",
            () => NamePage(TaskManagerSettingsPage.Processes, BuildProcessesPage()),
            TaskManagerGlyphCatalog.PROCESSES),
        new(
            TaskManagerSettingsPage.TrayIcon,
            Label: "Tray icon",
            () => NamePage(TaskManagerSettingsPage.TrayIcon, BuildTrayIconPage()),
            SettingsNavigationGlyphs.TrayIcon),
        new(
            TaskManagerSettingsPage.Performance,
            Label: "Performance",
            () => NamePage(TaskManagerSettingsPage.Performance, BuildPerformancePage()),
            SettingsNavigationGlyphs.Devices),
        new(
            TaskManagerSettingsPage.Hotkeys,
            Label: "Hotkeys",
            () => NamePage(TaskManagerSettingsPage.Hotkeys, BuildHotkeysPage()),
            SettingsNavigationGlyphs.Hotkeys),
        new(
            TaskManagerSettingsPage.Theme,
            Label: "Appearance",
            () => NamePage(TaskManagerSettingsPage.Theme, BuildThemePage()),
            SettingsNavigationGlyphs.Theme),
        new(
            TaskManagerSettingsPage.About,
            Label: "About",
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
        _resettableNumbers.Clear();
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
        StackPanel stack = PageStack(title: "General", palette);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(palette);
        stack.Children.Add(commonSection.BuildStartupCard());
        stack.Children.Add(BoolCard(
            title: "Autosave settings",
            description: "Save changes to the Task Manager settings file as they are made.",
            _settings.Autosave,
            value => _settings.Autosave = value,
            palette,
            searchKeywords: ["save settings automatically"],
            node: ControlMap.Settings.GeneralPage.Autosave));
        stack.Children.Add(BuildWindowManagementCard(palette));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Keyboard", palette));
        stack.Children.Add(BoolCard(
            title: "Replace Ctrl+Shift+Esc shortcut",
            description:
            "While this app is running, open it instead of Windows Task Manager when Ctrl+Shift+Esc is pressed.",
            _settings.OverrideWindowsTaskManagerHotkey,
            value => _settings.OverrideWindowsTaskManagerHotkey = value,
            palette,
            searchKeywords: ["Windows Task Manager hotkey shortcut control shift escape"],
            node: ControlMap.Settings.GeneralPage.OverrideWindowsTaskManagerHotkey));
        stack.Children.Add(BuildReplaceTaskManagerCard(palette));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Administrator actions", palette));
        stack.Children.Add(BoolCard(
            title: "Enable elevated termination at startup",
            description:
            "Request administrator approval when the app starts so it can end protected processes. When off, "
            + "approval is requested only the first time you act on a process that needs it.",
            _settings.EnableElevatedTerminationOnStartup,
            value => _settings.EnableElevatedTerminationOnStartup = value,
            palette,
            searchKeywords: ["elevated termination startup administrator UAC kill protected process eager"],
            node: ControlMap.Settings.GeneralPage.ElevatedTerminationOnStartup));
        stack.Children.Add(BoolCard(
            title: "Run admin actions directly when elevated",
            description:
            "When the app itself runs as administrator, perform admin actions in-process instead of through the "
            + "elevated helper. Off by default so behavior stays consistent whether or not the app is elevated.",
            _settings.BypassElevationBrokerWhenElevated,
            value => _settings.BypassElevationBrokerWhenElevated = value,
            palette,
            searchKeywords: ["bypass elevation broker elevated administrator in-process consistent helper"],
            node: ControlMap.Settings.GeneralPage.BypassElevationBroker));

        commonSection.AddInstallationSection(
            stack,
            [
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.LocalAppData,
                    Title = "Install for current user",
                    ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                    Elevated = false,
                    Install = static progress => AppServices.Installation.InstallToLocalAppData(progress: progress),
                    UninstallAsync = (_, progress) =>
                    {
                        _showUninstaller(
                            AppServices.InstallLayout.LocalAppDataInstallDirectory,
                            InstallScope.LocalAppData,
                            progress);
                        return Task.CompletedTask;
                    }
                },
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.ProgramFiles,
                    Title = "Install system-wide",
                    ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                    Elevated = true,
                    Install = static progress => AppServices.Installation.InstallSystemWide(progress: progress),
                    UninstallAsync = (_, progress) =>
                    {
                        _showUninstaller(
                            AppServices.InstallLayout.ProgramFilesInstallDirectory,
                            InstallScope.ProgramFiles,
                            progress);
                        return Task.CompletedTask;
                    }
                }
            ]);

        CreateRenderingSettingsSection(palette).AddCards(stack);
        return stack;
    }

    private StackPanel BuildProcessesPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(title: "Processes", palette);
        AddProcessGroupingCards(stack, palette);
        AddProcessZoomCards(stack, palette);
        AddLiveTotalCards(stack, palette);
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Columns and search", palette));
        stack.Children.Add(BoolCard(
            title: "Live column resizing",
            description: "Resize column contents while dragging instead of applying the new width on release.",
            _settings.EnableLiveDetailsColumnResizing,
            value => _settings.EnableLiveDetailsColumnResizing = value,
            palette,
            searchKeywords: ["column resize preview"],
            node: ControlMap.Settings.ProcessesPage.LiveColumnResizing));
        stack.Children.Add(BoolCard(
            title: "Left-align search bar",
            description:
            "Align the Processes search bar with the left edge of the page area instead of centering it in the window.",
            _settings.LeftAlignProcessSearchBar,
            value => _settings.LeftAlignProcessSearchBar = value,
            palette,
            searchKeywords: ["process search position", "search alignment"],
            node: ControlMap.Settings.ProcessesPage.LeftAlignSearchBar));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Actions", palette));
        stack.Children.Add(BoolCard(
            title: "Skip Explorer restart confirmation",
            description:
            "Restart Windows Explorer immediately from the Processes page without asking for confirmation.",
            _settings.SkipRestartExplorerConfirmation,
            value => _settings.SkipRestartExplorerConfirmation = value,
            palette,
            searchKeywords: ["restart explorer confirmation prompt warning"],
            node: ControlMap.Settings.ProcessesPage.SkipExplorerRestartConfirmation));
        return stack;
    }

    private void AddProcessGroupingCards(StackPanel stack, SettingsPalette palette)
    {
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Grouping", palette));
        Border semanticSubgroupRootCard = BoolCard(
            title: "Apply to subgroups",
            description:
            "Give every process with child processes the same layout. Its row shows the totals of its subtree, and "
            + "its first entry, Root, shows its own usage.",
            _settings.UseRootProcessForSemanticSubgroups,
            value => _settings.UseRootProcessForSemanticSubgroups = value,
            palette,
            searchKeywords: ["semantic subgroup nested child process tree root total sum"],
            node: ControlMap.Settings.ProcessesPage.UseRootProcessForSubgroups);
        semanticSubgroupRootCard.IsVisible =
            _settings.ProcessGroupingStyle == ProcessGroupingStyle.Semantic
            && _settings.UseRootProcessForSemanticGroups;
        Border semanticGroupRootCard = BoolCard(
            title: "Use root process as group row",
            description:
            "Show each semantic application group as its root process instead of a synthetic group row. The root "
            + "process shows the group's totals, and its first entry, Root, shows the root process's own usage.",
            _settings.UseRootProcessForSemanticGroups,
            value =>
            {
                _settings.UseRootProcessForSemanticGroups = value;
                semanticSubgroupRootCard.IsVisible = value;
            },
            palette,
            searchKeywords: ["semantic group root process total sum synthetic row aggregate"],
            node: ControlMap.Settings.ProcessesPage.UseRootProcessForGroups);
        semanticGroupRootCard.IsVisible =
            _settings.ProcessGroupingStyle == ProcessGroupingStyle.Semantic;
        Border windowsProcessesCard = BoolCard(
            title: "Group Windows processes",
            description:
            "List Windows system processes in their own Windows processes section, as Task Manager does. When off, "
            + "they appear under Apps or Background processes like any other process.",
            _settings.GroupWindowsProcesses,
            value => _settings.GroupWindowsProcesses = value,
            palette,
            searchKeywords: ["windows system processes section category svchost apps background"],
            node: ControlMap.Settings.ProcessesPage.GroupWindowsProcesses);
        windowsProcessesCard.IsVisible =
            _settings.ProcessGroupingStyle == ProcessGroupingStyle.Semantic;
        stack.Children.Add(ComboCard(
            title: "Process grouping style",
            description:
            "Choose how processes are organized when Group processes is enabled on the Processes page.",
            [
                (nameof(ProcessGroupingStyle.ParentProcess), "Parent process"),
                (nameof(ProcessGroupingStyle.Semantic), "Semantic application")
            ],
            _settings.ProcessGroupingStyle.ToString(),
            tag =>
            {
                if (!Enum.TryParse(tag, out ProcessGroupingStyle value)) return;

                _settings.ProcessGroupingStyle = value;
                semanticGroupRootCard.IsVisible = value == ProcessGroupingStyle.Semantic;
                semanticSubgroupRootCard.IsVisible = value == ProcessGroupingStyle.Semantic
                                                     && _settings.UseRootProcessForSemanticGroups;
                windowsProcessesCard.IsVisible = value == ProcessGroupingStyle.Semantic;
            },
            palette,
            searchKeywords: ["process tree application semantic parent ancestry group"],
            node: ControlMap.Settings.ProcessesPage.GroupingStyle));
        stack.Children.Add(semanticGroupRootCard);
        stack.Children.Add(semanticSubgroupRootCard);
        stack.Children.Add(windowsProcessesCard);
        Border semanticSectionExemptionCard = BoolCard(
            title: "Keep semantic sections expanded",
            description:
            "Keep Apps, Background processes, and Windows processes expanded when other process trees start collapsed.",
            _settings.ExpandSemanticSectionsByDefault,
            value => _settings.ExpandSemanticSectionsByDefault = value,
            palette,
            searchKeywords: ["apps background Windows process section category expand collapse"],
            node: ControlMap.Settings.ProcessesPage.ExpandSemanticSections);
        semanticSectionExemptionCard.IsVisible =
            _settings.ProcessTreeDefaultState == ProcessTreeDefaultState.Collapsed;
        stack.Children.Add(ComboCard(
            title: "Default process tree state",
            description: "Choose whether newly displayed process trees start collapsed or expanded.",
            [
                (nameof(ProcessTreeDefaultState.Collapsed), "Collapsed"),
                (nameof(ProcessTreeDefaultState.Expanded), "Expanded")
            ],
            _settings.ProcessTreeDefaultState.ToString(),
            tag =>
            {
                if (!Enum.TryParse(tag, out ProcessTreeDefaultState value)) return;

                _settings.ProcessTreeDefaultState = value;
                semanticSectionExemptionCard.IsVisible =
                    value == ProcessTreeDefaultState.Collapsed;
            },
            palette,
            searchKeywords: ["process tree default collapsed expanded start"],
            node: ControlMap.Settings.ProcessesPage.DefaultTreeState));
        stack.Children.Add(semanticSectionExemptionCard);
    }

    private void AddProcessZoomCards(StackPanel stack, SettingsPalette palette)
    {
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Zoom and stretch", palette));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            title: "Zoom",
            description:
            "Set the text size of process rows and the other Task Manager tables. "
            + $"{DescribeShortcut(TaskManagerGridShortcutAction.Zoom)} changes it from a table, and Reset returns it "
            + "to the baseline zoom.",
            () => _settings.GridFontSize,
            value => _settings.GridFontSize = value,
            () => _settings.GridFontSizeBaseline,
            AppSettings.GridFontSizeMinimum,
            AppSettings.GridFontSizeMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber zoom,
            DIPSuffix,
            ["grid text font size", "zoom"],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.Zoom.ID), zoom));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            title: "Baseline zoom",
            description:
            "Set the text size that Reset zoom returns to. "
            + $"{DescribeShortcut(TaskManagerGridShortcutAction.SetZoomBaseline)} makes the current zoom the "
            + "baseline from a table.",
            () => _settings.GridFontSizeBaseline,
            value => _settings.GridFontSizeBaseline = value,
            static () => AppSettings.GridFontSizeDefault,
            AppSettings.GridFontSizeMinimum,
            AppSettings.GridFontSizeMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber zoomBaseline,
            DIPSuffix,
            ["grid text font size", "zoom baseline default reset"],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.ZoomBaseline.ID), zoomBaseline));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            title: "Stretch",
            description:
            "Set the visible gap between table rows. "
            + $"{DescribeShortcut(TaskManagerGridShortcutAction.Stretch)} changes it from a table, and Reset returns "
            + "it to the baseline stretch.",
            () => _settings.GridRowSpacing,
            value => _settings.GridRowSpacing = value,
            () => _settings.GridRowSpacingBaseline,
            AppSettings.GridRowSpacingMinimum,
            AppSettings.GridRowSpacingMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber stretch,
            DIPSuffix,
            ["grid row spacing height", "stretch"],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.Stretch.ID), stretch));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            title: "Baseline stretch",
            description:
            "Set the row spacing that Reset stretch returns to. "
            + $"{DescribeShortcut(TaskManagerGridShortcutAction.SetStretchBaseline)} makes the current stretch the "
            + "baseline from a table.",
            () => _settings.GridRowSpacingBaseline,
            value => _settings.GridRowSpacingBaseline = value,
            static () => AppSettings.GridRowSpacingDefault,
            AppSettings.GridRowSpacingMinimum,
            AppSettings.GridRowSpacingMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber stretchBaseline,
            DIPSuffix,
            ["grid row spacing height", "stretch baseline default reset"],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.StretchBaseline.ID), stretchBaseline));
        stack.Children.Add(ComboCard(
            title: "Font weight",
            description: "Set the text weight used by process rows and column headers.",
            FontWeightItems(),
            _settings.GridFontWeight.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TaskManagerGridFontWeight value))
                    _settings.GridFontWeight = value;
            },
            palette,
            searchKeywords: ["grid text thickness", "bold"],
            node: ControlMap.Settings.ProcessesPage.FontWeight));
    }

    private void AddLiveTotalCards(StackPanel stack, SettingsPalette palette)
    {
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Live totals", palette));
        stack.Children.Add(BoolCard(
            title: "Show totals above column names",
            description:
            "Give live totals their own line above the column names, which doubles the height of the column "
            + "header row. Turn on a column's live total from its properties by right-clicking its header.",
            _settings.ShowLiveTotalsAboveColumnNames,
            value => _settings.ShowLiveTotalsAboveColumnNames = value,
            palette,
            searchKeywords: ["live total sum header stacked two lines above column name double height"],
            node: ControlMap.Settings.ProcessesPage.ShowTotalsAboveColumnNames));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            title: "Total font size",
            description: "Set the text size of live totals in the column headers.",
            () => _settings.LiveTotalFontSize,
            value => _settings.LiveTotalFontSize = value,
            static () => AppSettings.LiveTotalFontSizeDefault,
            AppSettings.LiveTotalFontSizeMinimum,
            AppSettings.LiveTotalFontSizeMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber fontSize,
            DIPSuffix,
            ["live total sum header text size"],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.TotalFontSize.ID), fontSize));
        stack.Children.Add(ComboCard(
            title: "Total font weight",
            description: "Set the text weight of live totals in the column headers.",
            FontWeightItems(),
            _settings.LiveTotalFontWeight.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TaskManagerGridFontWeight value))
                    _settings.LiveTotalFontWeight = value;
            },
            palette,
            searchKeywords: ["live total sum header text thickness bold"],
            node: ControlMap.Settings.ProcessesPage.TotalFontWeight));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            title: "Total width",
            description:
            "Squish live totals horizontally so wide values fit their columns. 100 % draws them unscaled.",
            () => _settings.LiveTotalHorizontalScale * PercentScale,
            value => _settings.LiveTotalHorizontalScale = value / PercentScale,
            static () => AppSettings.LiveTotalHorizontalScaleDefault * PercentScale,
            AppSettings.LiveTotalHorizontalScaleMinimum * PercentScale,
            AppSettings.LiveTotalHorizontalScaleMaximum * PercentScale,
            palette,
            ResetText,
            out SettingsResettableNumber horizontalScale,
            PercentSuffix,
            ["live total sum header squish condense narrow horizontal scale"],
            decimalPlaces: 0,
            step: LiveTotalHorizontalScaleStepPercent,
            node: ControlMap.Settings.ProcessesPage.TotalWidth.ID), horizontalScale));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            title: "Gap before column name",
            description:
            "Set the space between a live total and its column name when both share one line.",
            () => _settings.LiveTotalTextGap,
            value => _settings.LiveTotalTextGap = value,
            static () => AppSettings.LiveTotalTextGapDefault,
            AppSettings.LiveTotalTextGapMinimum,
            AppSettings.LiveTotalTextGapMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber textGap,
            DIPSuffix,
            ["live total sum header spacing gap"],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.TotalTextGap.ID), textGap));
    }

    /// <summary>
    /// Lists every table shortcut with the gestures its control map leaf declares, so the page shows exactly what the
    /// tables obey.
    /// </summary>
    private StackPanel BuildHotkeysPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(title: "Hotkeys", palette);
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Tables", palette));
        foreach (TaskManagerGridShortcutEntry entry in TaskManagerGridShortcuts.Entries)
        {
            string gestures = DescribeShortcut(entry.Action, ShortcutGestureSeparator);
            if (gestures.Length == 0) continue;

            TextBlock gestureText = TrayAppDotNETSettingsUI.Text(gestures, palette);
            gestureText.TextAlignment = TextAlignment.Right;
            stack.Children.Add(Card(
                TaskManagerGridShortcuts.GetTitle(entry.Action),
                TaskManagerGridShortcuts.GetDescription(entry.Action),
                gestureText,
                palette,
                searchKeywords: [gestures, "shortcut hotkey mouse wheel middle click keyboard"]));
        }

        return stack;
    }

    /// <summary>Formats every map gesture that performs an action, for example "Ctrl + Mouse wheel".</summary>
    private static string DescribeShortcut(TaskManagerGridShortcutAction action, string separator = " or ")
    {
        List<string> gestures = [];
        foreach (TaskManagerGridShortcut shortcut in TaskManagerGridShortcuts.All)
        {
            if (shortcut.Action == action) gestures.Add(TaskManagerGridShortcuts.FormatGesture(shortcut));
        }

        return string.Join(separator, gestures);
    }

    private static IReadOnlyList<(string Tag, string Text)> FontWeightItems() =>
    [
        (nameof(TaskManagerGridFontWeight.Thin), "Thin"),
        (nameof(TaskManagerGridFontWeight.ExtraLight), "Extra light"),
        (nameof(TaskManagerGridFontWeight.Light), "Light"),
        (nameof(TaskManagerGridFontWeight.SemiLight), "Semi-light"),
        (nameof(TaskManagerGridFontWeight.Normal), "Normal"),
        (nameof(TaskManagerGridFontWeight.Medium), "Medium"),
        (nameof(TaskManagerGridFontWeight.SemiBold), "Semi-bold"),
        (nameof(TaskManagerGridFontWeight.Bold), "Bold"),
        (nameof(TaskManagerGridFontWeight.ExtraBold), "Extra bold"),
        (nameof(TaskManagerGridFontWeight.Black), "Black")
    ];

    /// <summary>Keeps a resettable card in step with its settings until the page that owns it is torn down.</summary>
    private Border TrackResettableNumber(Border card, SettingsResettableNumber resettableNumber)
    {
        _resettableNumbers.Add(resettableNumber);
        AddPageCleanup(() => _resettableNumbers.Remove(resettableNumber));
        return card;
    }

    /// <summary>
    /// Builds the system-wide "Replace Windows Task Manager" card. Unlike a plain setting, this reflects
    /// the live registry redirect and applies changes through an elevated relaunch, so the toggle reverts
    /// when administrator approval is declined or the write fails.
    /// </summary>
    private Border BuildReplaceTaskManagerCard(SettingsPalette palette)
    {
        TaskManagerReplacementState initialState = TaskManagerReplacement.GetState();
        bool suppressReentry = false;

        SettingsToggle toggle = new SettingsToggle(palette) { IsChecked = initialState.IsEnabled }
            .MapTo(ControlMap.Settings.GeneralPage.ReplaceWindowsTaskManager);
        Border card = MutableCard(
            title: "Replace Windows Task Manager",
            DescribeReplacementState(initialState),
            toggle,
            palette,
            out TextBlock descriptionText,
            searchKeywords:
            [
                "replace windows task manager system wide image file execution options debugger",
                "ctrl alt del taskbar win+x administrator even when not running"
            ]);

        toggle.CheckedChanged += async (_, requestedEnabled) =>
        {
            if (suppressReentry) return;

            toggle.IsEnabled = false;
            TaskManagerReplacementResult result = await TaskManagerReplacement.SetEnabledElevatedAsync(requestedEnabled);
            toggle.IsEnabled = true;

            bool applied = requestedEnabled
                ? result == TaskManagerReplacementResult.Enabled
                : result == TaskManagerReplacementResult.Disabled;
            if (!applied)
            {
                // Restore the visual state without re-entering this handler
                suppressReentry = true;
                toggle.IsChecked = !requestedEnabled;
                suppressReentry = false;
            }

            descriptionText.Text = DescribeReplacementResult(result);
        };

        return card;
    }

    private static string DescribeReplacementState(TaskManagerReplacementState state)
    {
        if (state.IsEnabled)
            return "On. Ctrl+Shift+Esc, the Ctrl+Alt+Del screen, the taskbar menu, and Win+X all open this app "
                   + "instead of Windows Task Manager, even when this app is not already running. "
                   + "Changing this needs administrator approval.";
        if (state.PointsElsewhere)
            return "Windows Task Manager is currently redirected to another program. Turning this on points it "
                   + "at this app instead and needs administrator approval.";
        return "Redirect every Windows Task Manager launch to this app system-wide, so it opens even when this "
               + "app is not running. Applies to all users on this PC and needs administrator approval.";
    }

    private static string DescribeReplacementResult(TaskManagerReplacementResult result) => result switch
    {
        TaskManagerReplacementResult.Declined => "Administrator approval was declined, so nothing changed.",
        TaskManagerReplacementResult.Failed => "The change could not be applied. See the Task Manager log for details.",
        _ => DescribeReplacementState(TaskManagerReplacement.GetState())
    };

    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(title: "Tray icon", palette);
        stack.Children.Add(ComboCard(
            title: "Style",
            description: "Show only the latest value or a recency-weighted sliding history.",
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
            searchKeywords: ["graph current marquee history sliding"],
            node: ControlMap.Settings.TrayIconPage.GraphStyle));
        stack.Children.Add(ComboCard(
            title: "Data source",
            description: "Choose the system utilization measured by the tray graph.",
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
            searchKeywords: ["CPU processor core memory RAM metric"],
            node: ControlMap.Settings.TrayIconPage.DataSource));
        stack.Children.Add(BoolCard(
            title: "Show highest core trace",
            description: "Overlay highest-logical-processor utilization on the average CPU marquee graph.",
            _settings.ShowTrayCPUHighestCoreTrace,
            value => _settings.ShowTrayCPUHighestCoreTrace = value,
            palette,
            searchKeywords: ["CPU highest core logical processor secondary trace line overlay"],
            node: ControlMap.Settings.TrayIconPage.ShowHighestCoreTrace));
        return stack;
    }

    private StackPanel BuildPerformancePage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(title: "Performance", palette);
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "History and sampling", palette));
        stack.Children.Add(IntCard(
            title: "History length",
            description: "Keep each Performance graph's samples for this many minutes.",
            _settings.PerformanceHistoryLengthMinutes,
            PerformanceSamplingSettings.MinimumHistoryLengthMinutes,
            PerformanceSamplingSettings.MaximumHistoryLengthMinutes,
            value => _settings.PerformanceHistoryLengthMinutes = value,
            palette,
            suffix: " min",
            ["performance graph history retention minutes"],
            node: ControlMap.Settings.PerformancePage.HistoryLength));
        stack.Children.Add(IntCard(
            title: "Sampling interval",
            description: "Wait this many milliseconds between Performance samples.",
            _settings.PerformanceSampleIntervalMilliseconds,
            PerformanceSamplingSettings.MinimumSampleIntervalMilliseconds,
            PerformanceSamplingSettings.MaximumSampleIntervalMilliseconds,
            value => _settings.PerformanceSampleIntervalMilliseconds = value,
            palette,
            suffix: " ms",
            ["performance refresh update rate frequency milliseconds"],
            node: ControlMap.Settings.PerformancePage.SamplingInterval));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Graphs", palette));
        stack.Children.Add(BoolCard(
            title: "Fill graph areas",
            description: "Draw a translucent shaded area beneath Performance graph lines.",
            _settings.ShowPerformanceGraphUnderfill,
            value => _settings.ShowPerformanceGraphUnderfill = value,
            palette,
            searchKeywords: ["performance graph underfill shade translucent area"],
            node: ControlMap.Settings.PerformancePage.FillGraphAreas));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "CPU", palette));
        stack.Children.Add(BoolCard(
            title: "Show highest core trace",
            description: "Draw a thinner, dimmer highest-logical-processor trace behind the overall CPU graph.",
            _settings.ShowCPUHighestCoreTrace,
            value => _settings.ShowCPUHighestCoreTrace = value,
            palette,
            searchKeywords: ["CPU core logical processor utilization graph overlay"],
            node: ControlMap.Settings.PerformancePage.ShowHighestCoreTrace));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Memory", palette));
        stack.Children.Add(BoolCard(
            title: "Show memory module serial numbers",
            "Display each physical memory module's serial number in the Memory performance details. "
            + "Serial numbers are hidden by default.",
            _settings.ShowMemoryModuleSerialNumbers,
            value => _settings.ShowMemoryModuleSerialNumbers = value,
            palette,
            searchKeywords: ["RAM DIMM privacy serial number"],
            node: ControlMap.Settings.PerformancePage.ShowMemorySerialNumbers));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Device column", palette));
        stack.Children.Add(BuildDevicePriorityCard(palette));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Hardware names", palette));
        stack.Children.Add(BuildHardwareNameReplacementCard(palette));
        return stack;
    }

    private Border BuildHardwareNameReplacementCard(SettingsPalette palette)
    {
        StackPanel content = new();
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText(
            text: "Hardware name replacements",
            palette));
        content.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            "Apply case-insensitive .NET regular expression replacements to device hardware names. "
            + "Rules run from top to bottom, and replacements support $1 and ${name} captures.",
            palette));

        SettingsButton addButton = Button(text: "+ Add replacement", palette)
            .MapTo(ControlMap.Settings.PerformancePage.HardwareNameReplacements.AddReplacement);
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
            _taskManagerResources.AxamlTaskManagerSettings.HardwareNameRuleDeviceTypeWidth)
            .MapTo(ControlMap.Settings.PerformancePage.HardwareNameReplacements.Rule.DeviceCategory);
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
            rule.MatchPattern).MapTo(ControlMap.Settings.PerformancePage.HardwareNameReplacements.Rule.MatchPattern);
        matchPattern.MinWidth = _taskManagerResources.AxamlTaskManagerSettings
            .HardwareNameRuleTextMinimumWidth;
        matchPattern.PlaceholderText = "Regex match";
        matchPattern.TextChanged += (_, _) => UpdateHardwareNameReplacementMatchPattern(
            ruleIndex,
            matchPattern.Text ?? string.Empty);
        TrayAppDotNETToolTip.SetTip(
            matchPattern,
            tip: "Case-insensitive .NET regular expression matched against the hardware name.");

        TextBox replacement = TrayAppDotNETSettingsUI.TextBox(
            palette,
            double.NaN,
            rule.Replacement).MapTo(ControlMap.Settings.PerformancePage.HardwareNameReplacements.Rule.Replacement);
        replacement.MinWidth = _taskManagerResources.AxamlTaskManagerSettings
            .HardwareNameRuleTextMinimumWidth;
        replacement.PlaceholderText = "Replacement ($1)";
        replacement.TextChanged += (_, _) => UpdateHardwareNameReplacementValue(
            ruleIndex,
            replacement.Text ?? string.Empty);
        TrayAppDotNETToolTip.SetTip(
            replacement,
            tip: "Replacement text. Use $1 or ${name} to insert a regex capture.");

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
        deleteButton.MapTo(ControlMap.Settings.PerformancePage.HardwareNameReplacements.Rule.Delete);
        deleteButton.Click += (_, _) => DeleteHardwareNameReplacementRule(ruleIndex);
        TrayAppDotNETToolTip.SetTip(deleteButton, tip: "Delete replacement");

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
        Grid.SetColumn(matchPattern, value: 1);
        row.Children.Add(matchPattern);
        Grid.SetColumn(replacement, value: 2);
        row.Children.Add(replacement);
        Grid.SetColumn(deleteButton, value: 3);
        row.Children.Add(deleteButton);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            CornerRadius = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRuleRowCornerRadius,
            Padding = _taskManagerResources.AxamlTaskManagerSettings
                .HardwareNameRuleRowPadding,
            Child = row
        }.MapTo(ControlMap.Settings.PerformancePage.HardwareNameReplacements.Rule.ID);
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
            return;

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
            return;

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

        SettingsButton resetButton = TrayAppDotNETSettingsUI.Button(text: "Reset default priority", palette)
            .MapTo(ControlMap.Settings.PerformancePage.DevicePriority.ResetActions.ResetPriority);
        resetButton.IsEnabled = !priority.SequenceEqual(PerformanceDeviceOrdering.DefaultPriority);
        resetButton.Click += (_, _) => ResetPerformanceDevicePriority();
        SettingsButton resetDeviceOrderButton = TrayAppDotNETSettingsUI.Button(
            text: "Clear dragged device order",
            palette).MapTo(ControlMap.Settings.PerformancePage.DevicePriority.ResetActions.ClearDeviceOrder);
        _resetPerformanceDeviceOrderButton = resetDeviceOrderButton;
        resetDeviceOrderButton.IsEnabled = _settings.PerformanceDeviceOrder.Count > 0;
        resetDeviceOrderButton.Click += (_, _) => ResetPerformanceDeviceOrder();
        StackPanel resetActions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityButtonSpacing,
            Children = { resetButton, resetDeviceOrderButton }
        }.MapTo(ControlMap.Settings.PerformancePage.DevicePriority.ResetActions.ID);
        rows.Children.Add(resetActions);

        StackPanel content = new();
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText(text: "Default device priority", palette));
        content.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            text:
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

        SettingsButton moveUp = TrayAppDotNETSettingsUI.Button(text: "Move up", palette)
            .MapTo(ControlMap.Settings.PerformancePage.DevicePriority.Category.MoveUp);
        moveUp.IsEnabled = priorityIndex > 0;
        moveUp.Click += (_, _) => MovePerformanceDevicePriority(kind, offset: -1);
        SettingsButton moveDown = TrayAppDotNETSettingsUI.Button(text: "Move down", palette)
            .MapTo(ControlMap.Settings.PerformancePage.DevicePriority.Category.MoveDown);
        moveDown.IsEnabled = priorityIndex + 1 < priorityCount;
        moveDown.Click += (_, _) => MovePerformanceDevicePriority(kind, offset: 1);
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
        Grid.SetColumn(label, value: 1);
        row.Children.Add(label);
        Grid.SetColumn(actions, value: 2);
        row.Children.Add(actions);
        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            CornerRadius = _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityRowCornerRadius,
            Padding = _taskManagerResources.AxamlTaskManagerSettings.DevicePriorityRowPadding,
            Child = row
        }.MapTo(ControlMap.Settings.PerformancePage.DevicePriority.Category.ID);
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
        switch (eventArgs.PropertyName)
        {
            case nameof(AppSettings.PerformanceDeviceOrder) when _resetPerformanceDeviceOrderButton != null:
                _resetPerformanceDeviceOrderButton.IsEnabled = _settings.PerformanceDeviceOrder.Count > 0;
                return;
            // Table shortcuts change zoom and baselines while this window may be open
            case nameof(AppSettings.GridFontSize)
                or nameof(AppSettings.GridFontSizeBaseline)
                or nameof(AppSettings.GridRowSpacing)
                or nameof(AppSettings.GridRowSpacingBaseline):
                foreach (SettingsResettableNumber resettableNumber in _resettableNumbers)
                    resettableNumber.Refresh();
                return;
        }
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
            text: "Always on top",
            _settings.AlwaysOnTop,
            value =>
            {
                _settings.AlwaysOnTop = value;
                Topmost = value;
            },
            palette,
            ControlMap.Settings.GeneralPage.WindowManagement.AlwaysOnTop));
        options.Children.Add(CreateWindowManagementCheckBox(
            text: "Close to Tray",
            _settings.CloseToTray,
            value => _settings.CloseToTray = value,
            palette,
            ControlMap.Settings.GeneralPage.WindowManagement.CloseToTray));
        options.Children.Add(CreateWindowManagementCheckBox(
            text: "Minimize to Tray",
            _settings.MinimizeToTray,
            value => _settings.MinimizeToTray = value,
            palette,
            ControlMap.Settings.GeneralPage.WindowManagement.MinimizeToTray));

        StackPanel content = new();
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText(text: "Window management", palette));
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
        SettingsPalette palette,
        ControlMapNodeID node)
    {
        CheckBox checkBox = new CheckBox
        {
            Content = TrayAppDotNETSettingsUI.Text(text, palette),
            IsChecked = isChecked,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground)
        }.MapTo(node);
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
        StackPanel stack = PageStack(title: "Appearance", palette);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Theme", palette));
        stack.Children.Add(ComboCard(
            title: "Theme mode",
            description: "Choose whether Task Manager follows Windows or uses a fixed light or dark theme.",
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
            () => RebuildShell(TaskManagerSettingsPage.Theme),
            searchKeywords: ["light dark system"],
            node: ControlMap.Settings.ThemePage.ThemeMode));
        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Title)),
            L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Description)),
            _settings.UseWindows11SettingsNavigation,
            value => _settings.UseWindows11SettingsNavigation = value,
            palette,
            () => RebuildShell(TaskManagerSettingsPage.Theme),
            [L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_SearchKeywords))],
            ControlMap.Settings.ThemePage.Windows11Navigation));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Window", palette));
        stack.Children.Add(BoolCard(
            title: "Rounded corners",
            description: "Use rounded corners on Task Manager and its menus.",
            _settings.EnableRoundedCorners,
            value => _settings.EnableRoundedCorners = value,
            palette,
            () => RebuildShell(TaskManagerSettingsPage.Theme),
            ["square sharp corners"],
            ControlMap.Settings.ThemePage.RoundedCorners));
        stack.Children.Add(BoolCard(
            title: "Collapse navigation when narrow",
            description: "Show only navigation icons when the Task Manager window is narrower than 750 pixels.",
            _settings.CollapseSidebarWhenNarrow,
            value => _settings.CollapseSidebarWhenNarrow = value,
            palette,
            searchKeywords: ["sidebar left menu responsive"],
            node: ControlMap.Settings.ThemePage.CollapseNavigationWhenNarrow));
        stack.Children.Add(ComboCard(
            title: "Animations",
            description: "Choose whether interface animations follow Windows, remain disabled, or remain enabled.",
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
            ApplyAnimationMode,
            searchKeywords: ["motion transitions"],
            node: ControlMap.Settings.ThemePage.Animations));

        stack.Children.Add(IntCard(
            title: "Tooltip delay",
            description: "Set how long the pointer must hover before a tooltip appears.",
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
            suffix: " ms",
            ["hover tooltip timing"],
            node: ControlMap.Settings.ThemePage.ToolTipDelay));

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
                UpdateSettings = _settings,
                UpdateService = static () => AppServices.UpdateCheckService,
                ConfirmAsync = ConfirmAsync,
                PromptOwner = () => this,
                Log = TADNLog.Log,
                SupportsFlyoutUpdateButton = false,
                RebuildAboutPage = () => RebuildShell(TaskManagerSettingsPage.About),
                UpdatePromptNode = ControlMap.SettingsUpdateConfirmation.ID
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
            GetStartMinimized = () => _settings.StartMinimized,
            SetStartMinimized = value => _settings.StartMinimized = value,
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
