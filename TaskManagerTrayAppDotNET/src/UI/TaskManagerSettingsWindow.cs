using System.ComponentModel;
using System.Globalization;
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
    private const double PercentScale = 100;
    private const double GridTypographyStep = 0.5;
    private const double LiveTotalHorizontalScaleStepPercent = 5;
    private const string ShortcutGestureSeparator = "\n";

    private static string ResetText => L(nameof(AppStrings.Common_Reset));

    private static string DIPSuffix => L(nameof(AppStrings.Common_DIPSuffix));

    private static string PercentSuffix => L(nameof(AppStrings.Common_PercentSuffix));

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
        ConfigureCompactSettingsWindow(title: L(nameof(AppStrings.SettingsWindow_Title)), icon: null);
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

    protected override string HeaderText => L(nameof(AppStrings.SettingsWindow_Header));

    protected override string OpenSettingsFolderText => L(nameof(AppStrings.SettingsWindow_OpenSettingsFolder));

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
            Label: L(nameof(AppStrings.Settings_Common_Page_General)),
            () => NamePage(TaskManagerSettingsPage.General, BuildGeneralPage()),
            SettingsNavigationGlyphs.General),
        new(
            TaskManagerSettingsPage.Processes,
            Label: L(nameof(AppStrings.Settings_Common_Page_Processes)),
            () => NamePage(TaskManagerSettingsPage.Processes, BuildProcessesPage()),
            TaskManagerGlyphCatalog.PROCESSES),
        new(
            TaskManagerSettingsPage.TrayIcon,
            Label: L(nameof(AppStrings.Settings_Common_Page_TrayIcon)),
            () => NamePage(TaskManagerSettingsPage.TrayIcon, BuildTrayIconPage()),
            SettingsNavigationGlyphs.TrayIcon),
        new(
            TaskManagerSettingsPage.Performance,
            Label: L(nameof(AppStrings.Settings_Common_Page_Performance)),
            () => NamePage(TaskManagerSettingsPage.Performance, BuildPerformancePage()),
            SettingsNavigationGlyphs.Devices),
        new(
            TaskManagerSettingsPage.Hotkeys,
            Label: L(nameof(AppStrings.Settings_Common_Page_Hotkeys)),
            () => NamePage(TaskManagerSettingsPage.Hotkeys, BuildHotkeysPage()),
            SettingsNavigationGlyphs.Hotkeys),
        new(
            TaskManagerSettingsPage.Theme,
            Label: L(nameof(AppStrings.Settings_Common_Page_Theme)),
            () => NamePage(TaskManagerSettingsPage.Theme, BuildThemePage()),
            SettingsNavigationGlyphs.Theme),
        new(
            TaskManagerSettingsPage.About,
            Label: L(nameof(AppStrings.Settings_Common_Page_About)),
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
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_General_SectionHeader)), palette);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(palette);
        stack.Children.Add(commonSection.BuildLanguageCard());
        stack.Children.Add(commonSection.BuildStartupCard());
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_Autosave_Title)),
            L(nameof(AppStrings.Settings_General_Autosave_Description)),
            _settings.Autosave,
            value => _settings.Autosave = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_General_Autosave_SearchKeywords))],
            node: ControlMap.Settings.GeneralPage.Autosave));
        stack.Children.Add(BuildWindowManagementCard(palette));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_General_Keyboard_Header)),
            palette));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_OverrideWindowsTaskManagerHotkey_Title)),
            L(nameof(AppStrings.Settings_General_OverrideWindowsTaskManagerHotkey_Description)),
            _settings.OverrideWindowsTaskManagerHotkey,
            value => _settings.OverrideWindowsTaskManagerHotkey = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_General_OverrideWindowsTaskManagerHotkey_SearchKeywords))],
            node: ControlMap.Settings.GeneralPage.OverrideWindowsTaskManagerHotkey));
        stack.Children.Add(BuildReplaceTaskManagerCard(palette));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_General_AdministratorActions_Header)),
            palette));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_ElevatedTerminationOnStartup_Title)),
            L(nameof(AppStrings.Settings_General_ElevatedTerminationOnStartup_Description)),
            _settings.EnableElevatedTerminationOnStartup,
            value => _settings.EnableElevatedTerminationOnStartup = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_General_ElevatedTerminationOnStartup_SearchKeywords))],
            node: ControlMap.Settings.GeneralPage.ElevatedTerminationOnStartup));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_BypassElevationBroker_Title)),
            L(nameof(AppStrings.Settings_General_BypassElevationBroker_Description)),
            _settings.BypassElevationBrokerWhenElevated,
            value => _settings.BypassElevationBrokerWhenElevated = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_General_BypassElevationBroker_SearchKeywords))],
            node: ControlMap.Settings.GeneralPage.BypassElevationBroker));

        commonSection.AddInstallationSection(
            stack,
            [
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.LocalAppData,
                    Title = L(nameof(AppStrings.Settings_General_LocalUser_Title)),
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
                    Title = L(nameof(AppStrings.Settings_General_SystemWide_Title)),
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
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Processes_SectionHeader)), palette);
        AddProcessGroupingCards(stack, palette);
        AddProcessZoomCards(stack, palette);
        AddLiveTotalCards(stack, palette);
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Processes_ColumnsAndSearch_Header)),
            palette));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Processes_LiveColumnResizing_Title)),
            L(nameof(AppStrings.Settings_Processes_LiveColumnResizing_Description)),
            _settings.EnableLiveDetailsColumnResizing,
            value => _settings.EnableLiveDetailsColumnResizing = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_LiveColumnResizing_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.LiveColumnResizing));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Processes_LeftAlignSearchBar_Title)),
            L(nameof(AppStrings.Settings_Processes_LeftAlignSearchBar_Description)),
            _settings.LeftAlignProcessSearchBar,
            value => _settings.LeftAlignProcessSearchBar = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_LeftAlignSearchBar_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.LeftAlignSearchBar));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Processes_Actions_Header)),
            palette));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Processes_SkipExplorerRestartConfirmation_Title)),
            L(nameof(AppStrings.Settings_Processes_SkipExplorerRestartConfirmation_Description)),
            _settings.SkipRestartExplorerConfirmation,
            value => _settings.SkipRestartExplorerConfirmation = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_SkipExplorerRestartConfirmation_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.SkipExplorerRestartConfirmation));
        return stack;
    }

    private void AddProcessGroupingCards(StackPanel stack, SettingsPalette palette)
    {
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Processes_Grouping_Header)),
            palette));
        Border semanticSubgroupRootCard = BoolCard(
            L(nameof(AppStrings.Settings_Processes_UseRootProcessForSubgroups_Title)),
            L(nameof(AppStrings.Settings_Processes_UseRootProcessForSubgroups_Description)),
            _settings.UseRootProcessForSemanticSubgroups,
            value => _settings.UseRootProcessForSemanticSubgroups = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_UseRootProcessForSubgroups_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.UseRootProcessForSubgroups);
        semanticSubgroupRootCard.IsVisible =
            _settings.ProcessGroupingStyle == ProcessGroupingStyle.Semantic
            && _settings.UseRootProcessForSemanticGroups;
        Border semanticGroupRootCard = BoolCard(
            L(nameof(AppStrings.Settings_Processes_UseRootProcessForGroups_Title)),
            L(nameof(AppStrings.Settings_Processes_UseRootProcessForGroups_Description)),
            _settings.UseRootProcessForSemanticGroups,
            value =>
            {
                _settings.UseRootProcessForSemanticGroups = value;
                semanticSubgroupRootCard.IsVisible = value;
            },
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_UseRootProcessForGroups_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.UseRootProcessForGroups);
        semanticGroupRootCard.IsVisible =
            _settings.ProcessGroupingStyle == ProcessGroupingStyle.Semantic;
        Border windowsProcessesCard = BoolCard(
            L(nameof(AppStrings.Settings_Processes_GroupWindowsProcesses_Title)),
            L(nameof(AppStrings.Settings_Processes_GroupWindowsProcesses_Description)),
            _settings.GroupWindowsProcesses,
            value => _settings.GroupWindowsProcesses = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_GroupWindowsProcesses_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.GroupWindowsProcesses);
        windowsProcessesCard.IsVisible =
            _settings.ProcessGroupingStyle == ProcessGroupingStyle.Semantic;
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_Processes_GroupingStyle_Title)),
            L(nameof(AppStrings.Settings_Processes_GroupingStyle_Description)),
            [
                (nameof(ProcessGroupingStyle.ParentProcess),
                    L(nameof(AppStrings.Settings_Processes_GroupingStyle_ParentProcess))),
                (nameof(ProcessGroupingStyle.Semantic), L(nameof(AppStrings.Settings_Processes_GroupingStyle_Semantic)))
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
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_GroupingStyle_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.GroupingStyle));
        stack.Children.Add(semanticGroupRootCard);
        stack.Children.Add(semanticSubgroupRootCard);
        stack.Children.Add(windowsProcessesCard);
        Border semanticSectionExemptionCard = BoolCard(
            L(nameof(AppStrings.Settings_Processes_ExpandSemanticSections_Title)),
            L(nameof(AppStrings.Settings_Processes_ExpandSemanticSections_Description)),
            _settings.ExpandSemanticSectionsByDefault,
            value => _settings.ExpandSemanticSectionsByDefault = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_ExpandSemanticSections_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.ExpandSemanticSections);
        semanticSectionExemptionCard.IsVisible =
            _settings.ProcessTreeDefaultState == ProcessTreeDefaultState.Collapsed;
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_Processes_DefaultTreeState_Title)),
            L(nameof(AppStrings.Settings_Processes_DefaultTreeState_Description)),
            [
                (nameof(ProcessTreeDefaultState.Collapsed),
                    L(nameof(AppStrings.Settings_Processes_DefaultTreeState_Collapsed))),
                (nameof(ProcessTreeDefaultState.Expanded),
                    L(nameof(AppStrings.Settings_Processes_DefaultTreeState_Expanded)))
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
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_DefaultTreeState_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.DefaultTreeState));
        stack.Children.Add(semanticSectionExemptionCard);
    }

    private void AddProcessZoomCards(StackPanel stack, SettingsPalette palette)
    {
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Processes_ZoomAndStretch_Header)),
            palette));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            L(nameof(AppStrings.Settings_Processes_Zoom_Title)),
            DescribeWithShortcut(
                nameof(AppStrings.Settings_Processes_Zoom_Description_Format),
                TaskManagerGridShortcutAction.Zoom),
            () => _settings.GridFontSize,
            value => _settings.GridFontSize = value,
            () => _settings.GridFontSizeBaseline,
            AppSettings.GridFontSizeMinimum,
            AppSettings.GridFontSizeMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber zoom,
            DIPSuffix,
            [L(nameof(AppStrings.Settings_Processes_Zoom_SearchKeywords))],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.Zoom.ID), zoom));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            L(nameof(AppStrings.Settings_Processes_ZoomBaseline_Title)),
            DescribeWithShortcut(
                nameof(AppStrings.Settings_Processes_ZoomBaseline_Description_Format),
                TaskManagerGridShortcutAction.SetZoomBaseline),
            () => _settings.GridFontSizeBaseline,
            value => _settings.GridFontSizeBaseline = value,
            static () => AppSettings.GridFontSizeDefault,
            AppSettings.GridFontSizeMinimum,
            AppSettings.GridFontSizeMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber zoomBaseline,
            DIPSuffix,
            [L(nameof(AppStrings.Settings_Processes_ZoomBaseline_SearchKeywords))],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.ZoomBaseline.ID), zoomBaseline));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            L(nameof(AppStrings.Settings_Processes_Stretch_Title)),
            DescribeWithShortcut(
                nameof(AppStrings.Settings_Processes_Stretch_Description_Format),
                TaskManagerGridShortcutAction.Stretch),
            () => _settings.GridRowSpacing,
            value => _settings.GridRowSpacing = value,
            () => _settings.GridRowSpacingBaseline,
            AppSettings.GridRowSpacingMinimum,
            AppSettings.GridRowSpacingMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber stretch,
            DIPSuffix,
            [L(nameof(AppStrings.Settings_Processes_Stretch_SearchKeywords))],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.Stretch.ID), stretch));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            L(nameof(AppStrings.Settings_Processes_StretchBaseline_Title)),
            DescribeWithShortcut(
                nameof(AppStrings.Settings_Processes_StretchBaseline_Description_Format),
                TaskManagerGridShortcutAction.SetStretchBaseline),
            () => _settings.GridRowSpacingBaseline,
            value => _settings.GridRowSpacingBaseline = value,
            static () => AppSettings.GridRowSpacingDefault,
            AppSettings.GridRowSpacingMinimum,
            AppSettings.GridRowSpacingMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber stretchBaseline,
            DIPSuffix,
            [L(nameof(AppStrings.Settings_Processes_StretchBaseline_SearchKeywords))],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.StretchBaseline.ID), stretchBaseline));
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_Processes_FontWeight_Title)),
            L(nameof(AppStrings.Settings_Processes_FontWeight_Description)),
            FontWeightItems(),
            _settings.GridFontWeight.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TaskManagerGridFontWeight value))
                    _settings.GridFontWeight = value;
            },
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_FontWeight_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.FontWeight));
    }

    private void AddLiveTotalCards(StackPanel stack, SettingsPalette palette)
    {
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Processes_LiveTotals_Header)),
            palette));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Processes_ShowTotalsAboveColumnNames_Title)),
            L(nameof(AppStrings.Settings_Processes_ShowTotalsAboveColumnNames_Description)),
            _settings.ShowLiveTotalsAboveColumnNames,
            value => _settings.ShowLiveTotalsAboveColumnNames = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_ShowTotalsAboveColumnNames_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.ShowTotalsAboveColumnNames));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            L(nameof(AppStrings.Settings_Processes_TotalFontSize_Title)),
            L(nameof(AppStrings.Settings_Processes_TotalFontSize_Description)),
            () => _settings.LiveTotalFontSize,
            value => _settings.LiveTotalFontSize = value,
            static () => AppSettings.LiveTotalFontSizeDefault,
            AppSettings.LiveTotalFontSizeMinimum,
            AppSettings.LiveTotalFontSizeMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber fontSize,
            DIPSuffix,
            [L(nameof(AppStrings.Settings_Processes_TotalFontSize_SearchKeywords))],
            step: GridTypographyStep,
            node: ControlMap.Settings.ProcessesPage.TotalFontSize.ID), fontSize));
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_Processes_TotalFontWeight_Title)),
            L(nameof(AppStrings.Settings_Processes_TotalFontWeight_Description)),
            FontWeightItems(),
            _settings.LiveTotalFontWeight.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TaskManagerGridFontWeight value))
                    _settings.LiveTotalFontWeight = value;
            },
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Processes_TotalFontWeight_SearchKeywords))],
            node: ControlMap.Settings.ProcessesPage.TotalFontWeight));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            L(nameof(AppStrings.Settings_Processes_TotalWidth_Title)),
            L(nameof(AppStrings.Settings_Processes_TotalWidth_Description)),
            () => _settings.LiveTotalHorizontalScale * PercentScale,
            value => _settings.LiveTotalHorizontalScale = value / PercentScale,
            static () => AppSettings.LiveTotalHorizontalScaleDefault * PercentScale,
            AppSettings.LiveTotalHorizontalScaleMinimum * PercentScale,
            AppSettings.LiveTotalHorizontalScaleMaximum * PercentScale,
            palette,
            ResetText,
            out SettingsResettableNumber horizontalScale,
            PercentSuffix,
            [L(nameof(AppStrings.Settings_Processes_TotalWidth_SearchKeywords))],
            decimalPlaces: 0,
            step: LiveTotalHorizontalScaleStepPercent,
            node: ControlMap.Settings.ProcessesPage.TotalWidth.ID), horizontalScale));
        stack.Children.Add(TrackResettableNumber(ResettableDoubleCard(
            L(nameof(AppStrings.Settings_Processes_TotalTextGap_Title)),
            L(nameof(AppStrings.Settings_Processes_TotalTextGap_Description)),
            () => _settings.LiveTotalTextGap,
            value => _settings.LiveTotalTextGap = value,
            static () => AppSettings.LiveTotalTextGapDefault,
            AppSettings.LiveTotalTextGapMinimum,
            AppSettings.LiveTotalTextGapMaximum,
            palette,
            ResetText,
            out SettingsResettableNumber textGap,
            DIPSuffix,
            [L(nameof(AppStrings.Settings_Processes_TotalTextGap_SearchKeywords))],
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
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Hotkeys_SectionHeader)), palette);
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Hotkeys_Tables_Header)),
            palette));
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
                searchKeywords: [gestures, L(nameof(AppStrings.Settings_Hotkeys_Tables_SearchKeywords))]));
        }

        return stack;
    }

    /// <summary>Formats every map gesture that performs an action, for example "Ctrl + Mouse wheel".</summary>
    private static string DescribeShortcut(TaskManagerGridShortcutAction action, string? separator = null)
    {
        List<string> gestures = [];
        foreach (TaskManagerGridShortcut shortcut in TaskManagerGridShortcuts.All)
        {
            if (shortcut.Action == action) gestures.Add(TaskManagerGridShortcuts.FormatGesture(shortcut));
        }

        return string.Join(separator ?? L(nameof(AppStrings.Settings_Hotkeys_Gesture_OrSeparator)), gestures);
    }

    /// <summary>Fills a description format whose {0} names the table gestures that perform an action.</summary>
    private static string DescribeWithShortcut(string formatKey, TaskManagerGridShortcutAction action) =>
        string.Format(CultureInfo.CurrentCulture, L(formatKey), DescribeShortcut(action));

    private static IReadOnlyList<(string Tag, string Text)> FontWeightItems() =>
    [
        (nameof(TaskManagerGridFontWeight.Thin), L(nameof(AppStrings.Settings_Processes_FontWeight_Thin))),
        (nameof(TaskManagerGridFontWeight.ExtraLight), L(nameof(AppStrings.Settings_Processes_FontWeight_ExtraLight))),
        (nameof(TaskManagerGridFontWeight.Light), L(nameof(AppStrings.Settings_Processes_FontWeight_Light))),
        (nameof(TaskManagerGridFontWeight.SemiLight), L(nameof(AppStrings.Settings_Processes_FontWeight_SemiLight))),
        (nameof(TaskManagerGridFontWeight.Normal), L(nameof(AppStrings.Settings_Processes_FontWeight_Normal))),
        (nameof(TaskManagerGridFontWeight.Medium), L(nameof(AppStrings.Settings_Processes_FontWeight_Medium))),
        (nameof(TaskManagerGridFontWeight.SemiBold), L(nameof(AppStrings.Settings_Processes_FontWeight_SemiBold))),
        (nameof(TaskManagerGridFontWeight.Bold), L(nameof(AppStrings.Settings_Processes_FontWeight_Bold))),
        (nameof(TaskManagerGridFontWeight.ExtraBold), L(nameof(AppStrings.Settings_Processes_FontWeight_ExtraBold))),
        (nameof(TaskManagerGridFontWeight.Black), L(nameof(AppStrings.Settings_Processes_FontWeight_Black)))
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
            L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_Title)),
            DescribeReplacementState(initialState),
            toggle,
            palette,
            out TextBlock descriptionText,
            searchKeywords: [L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_SearchKeywords))]);

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
            return L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_EnabledDescription));
        if (state.PointsElsewhere)
        {
            // Name the program another tool registered; a path with no file name keeps the generic wording
            string redirectTarget = Path.GetFileName(state.RegisteredExecutable?.Trim()) is { Length: > 0 } fileName
                ? fileName
                : L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_AnotherProgram));
            return string.Format(
                CultureInfo.CurrentCulture,
                L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_RedirectedDescription_Format)),
                redirectTarget);
        }
        return L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_Description));
    }

    private static string DescribeReplacementResult(TaskManagerReplacementResult result) => result switch
    {
        TaskManagerReplacementResult.Declined =>
            L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_Declined)),
        TaskManagerReplacementResult.Failed => L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_Failed)),
        _ => DescribeReplacementState(TaskManagerReplacement.GetState())
    };

    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_TrayIcon_SectionHeader)), palette);
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_TrayIcon_GraphStyle_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_GraphStyle_Description)),
            [
                (nameof(TrayGraphStyle.Current), L(nameof(AppStrings.Settings_TrayIcon_GraphStyle_Current))),
                (nameof(TrayGraphStyle.Marquee), L(nameof(AppStrings.Settings_TrayIcon_GraphStyle_Marquee)))
            ],
            _settings.TrayGraphStyle.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayGraphStyle value))
                    _settings.TrayGraphStyle = value;
            },
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_GraphStyle_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.GraphStyle));
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_TrayIcon_DataSource_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_DataSource_Description)),
            [
                (nameof(TrayGraphDataSource.CPUAverage), L(nameof(AppStrings.Settings_TrayIcon_DataSource_CPUAverage))),
                (nameof(TrayGraphDataSource.CPUHighestCore),
                    L(nameof(AppStrings.Settings_TrayIcon_DataSource_CPUHighestCore))),
                (nameof(TrayGraphDataSource.Memory), L(nameof(AppStrings.Settings_TrayIcon_DataSource_Memory)))
            ],
            _settings.TrayGraphDataSource.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayGraphDataSource value))
                    _settings.TrayGraphDataSource = value;
            },
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_DataSource_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.DataSource));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_ShowHighestCoreTrace_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_ShowHighestCoreTrace_Description)),
            _settings.ShowTrayCPUHighestCoreTrace,
            value => _settings.ShowTrayCPUHighestCoreTrace = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ShowHighestCoreTrace_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.ShowHighestCoreTrace));
        return stack;
    }

    private StackPanel BuildPerformancePage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Performance_SectionHeader)), palette);
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Performance_HistoryAndSampling_Header)),
            palette));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_Performance_HistoryLength_Title)),
            L(nameof(AppStrings.Settings_Performance_HistoryLength_Description)),
            _settings.PerformanceHistoryLengthMinutes,
            PerformanceSamplingSettings.MinimumHistoryLengthMinutes,
            PerformanceSamplingSettings.MaximumHistoryLengthMinutes,
            value => _settings.PerformanceHistoryLengthMinutes = value,
            palette,
            suffix: L(nameof(AppStrings.Common_MinutesSuffix)),
            [L(nameof(AppStrings.Settings_Performance_HistoryLength_SearchKeywords))],
            node: ControlMap.Settings.PerformancePage.HistoryLength));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_Performance_SamplingInterval_Title)),
            L(nameof(AppStrings.Settings_Performance_SamplingInterval_Description)),
            _settings.PerformanceSampleIntervalMilliseconds,
            PerformanceSamplingSettings.MinimumSampleIntervalMilliseconds,
            PerformanceSamplingSettings.MaximumSampleIntervalMilliseconds,
            value => _settings.PerformanceSampleIntervalMilliseconds = value,
            palette,
            suffix: L(nameof(AppStrings.Common_MillisecondsSuffix)),
            [L(nameof(AppStrings.Settings_Performance_SamplingInterval_SearchKeywords))],
            node: ControlMap.Settings.PerformancePage.SamplingInterval));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Performance_Graphs_Header)),
            palette));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Performance_FillGraphAreas_Title)),
            L(nameof(AppStrings.Settings_Performance_FillGraphAreas_Description)),
            _settings.ShowPerformanceGraphUnderfill,
            value => _settings.ShowPerformanceGraphUnderfill = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Performance_FillGraphAreas_SearchKeywords))],
            node: ControlMap.Settings.PerformancePage.FillGraphAreas));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Performance_CPU_Header)),
            palette));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Performance_ShowHighestCoreTrace_Title)),
            L(nameof(AppStrings.Settings_Performance_ShowHighestCoreTrace_Description)),
            _settings.ShowCPUHighestCoreTrace,
            value => _settings.ShowCPUHighestCoreTrace = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Performance_ShowHighestCoreTrace_SearchKeywords))],
            node: ControlMap.Settings.PerformancePage.ShowHighestCoreTrace));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Performance_Memory_Header)),
            palette));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Performance_ShowMemorySerialNumbers_Title)),
            L(nameof(AppStrings.Settings_Performance_ShowMemorySerialNumbers_Description)),
            _settings.ShowMemoryModuleSerialNumbers,
            value => _settings.ShowMemoryModuleSerialNumbers = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Performance_ShowMemorySerialNumbers_SearchKeywords))],
            node: ControlMap.Settings.PerformancePage.ShowMemorySerialNumbers));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Performance_DeviceColumn_Header)),
            palette));
        stack.Children.Add(BuildDevicePriorityCard(palette));
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Performance_HardwareNames_Header)),
            palette));
        stack.Children.Add(BuildHardwareNameReplacementCard(palette));
        return stack;
    }

    private Border BuildHardwareNameReplacementCard(SettingsPalette palette)
    {
        StackPanel content = new();
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText(
            L(nameof(AppStrings.Settings_Performance_HardwareNameReplacements_Title)),
            palette));
        // NOTE: never format this text; its $1 and ${name} are literal regex capture syntax
        content.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(AppStrings.Settings_Performance_HardwareNameReplacements_Description)),
            palette));

        SettingsButton addButton = Button(
                L(nameof(AppStrings.Settings_Performance_HardwareNameReplacements_AddReplacement)),
                palette)
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
            [L(nameof(AppStrings.Settings_Performance_HardwareNameReplacements_SearchKeywords))]);
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
        matchPattern.PlaceholderText =
            L(nameof(AppStrings.Settings_Performance_HardwareNameReplacements_MatchPattern_Placeholder));
        matchPattern.TextChanged += (_, _) => UpdateHardwareNameReplacementMatchPattern(
            ruleIndex,
            matchPattern.Text ?? string.Empty);
        TrayAppDotNETToolTip.SetTip(
            matchPattern,
            L(nameof(AppStrings.Settings_Performance_HardwareNameReplacements_MatchPattern_Tooltip)));

        TextBox replacement = TrayAppDotNETSettingsUI.TextBox(
            palette,
            double.NaN,
            rule.Replacement).MapTo(ControlMap.Settings.PerformancePage.HardwareNameReplacements.Rule.Replacement);
        replacement.MinWidth = _taskManagerResources.AxamlTaskManagerSettings
            .HardwareNameRuleTextMinimumWidth;
        replacement.PlaceholderText =
            L(nameof(AppStrings.Settings_Performance_HardwareNameReplacements_Replacement_Placeholder));
        replacement.TextChanged += (_, _) => UpdateHardwareNameReplacementValue(
            ruleIndex,
            replacement.Text ?? string.Empty);
        TrayAppDotNETToolTip.SetTip(
            replacement,
            L(nameof(AppStrings.Settings_Performance_HardwareNameReplacements_Replacement_Tooltip)));

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
        TrayAppDotNETToolTip.SetTip(
            deleteButton,
            L(nameof(AppStrings.Settings_Performance_HardwareNameReplacements_Delete_Tooltip)));

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

        SettingsButton resetButton = TrayAppDotNETSettingsUI.Button(
                L(nameof(AppStrings.Settings_Performance_DevicePriority_ResetPriority)),
                palette)
            .MapTo(ControlMap.Settings.PerformancePage.DevicePriority.ResetActions.ResetPriority);
        resetButton.IsEnabled = !priority.SequenceEqual(PerformanceDeviceOrdering.DefaultPriority);
        resetButton.Click += (_, _) => ResetPerformanceDevicePriority();
        SettingsButton resetDeviceOrderButton = TrayAppDotNETSettingsUI.Button(
            L(nameof(AppStrings.Settings_Performance_DevicePriority_ClearDeviceOrder)),
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
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText(
            L(nameof(AppStrings.Settings_Performance_DevicePriority_Title)),
            palette));
        content.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(AppStrings.Settings_Performance_DevicePriority_Description)),
            palette));
        content.Children.Add(rows);
        return RawCard(
            content,
            palette,
            [L(nameof(AppStrings.Settings_Performance_DevicePriority_SearchKeywords))]);
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

        SettingsButton moveUp = TrayAppDotNETSettingsUI.Button(
                L(nameof(AppStrings.Settings_Performance_DevicePriority_MoveUp)),
                palette)
            .MapTo(ControlMap.Settings.PerformancePage.DevicePriority.Category.MoveUp);
        moveUp.IsEnabled = priorityIndex > 0;
        moveUp.Click += (_, _) => MovePerformanceDevicePriority(kind, offset: -1);
        SettingsButton moveDown = TrayAppDotNETSettingsUI.Button(
                L(nameof(AppStrings.Settings_Performance_DevicePriority_MoveDown)),
                palette)
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
        PerformanceDeviceKind.CPU => L(nameof(AppStrings.Settings_Performance_DeviceKind_CPU)),
        PerformanceDeviceKind.Memory => L(nameof(AppStrings.Settings_Performance_DeviceKind_Memory)),
        PerformanceDeviceKind.GPU => L(nameof(AppStrings.Settings_Performance_DeviceKind_GPU)),
        PerformanceDeviceKind.Network => L(nameof(AppStrings.Settings_Performance_DeviceKind_Network)),
        PerformanceDeviceKind.Disk => L(nameof(AppStrings.Settings_Performance_DeviceKind_Disk)),
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
            L(nameof(AppStrings.Settings_General_WindowManagement_AlwaysOnTop)),
            _settings.AlwaysOnTop,
            value =>
            {
                _settings.AlwaysOnTop = value;
                Topmost = value;
            },
            palette,
            ControlMap.Settings.GeneralPage.WindowManagement.AlwaysOnTop));
        options.Children.Add(CreateWindowManagementCheckBox(
            L(nameof(AppStrings.Settings_General_WindowManagement_CloseToTray)),
            _settings.CloseToTray,
            value => _settings.CloseToTray = value,
            palette,
            ControlMap.Settings.GeneralPage.WindowManagement.CloseToTray));
        options.Children.Add(CreateWindowManagementCheckBox(
            L(nameof(AppStrings.Settings_General_WindowManagement_MinimizeToTray)),
            _settings.MinimizeToTray,
            value => _settings.MinimizeToTray = value,
            palette,
            ControlMap.Settings.GeneralPage.WindowManagement.MinimizeToTray));

        StackPanel content = new();
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText(
            L(nameof(AppStrings.Settings_General_WindowManagement_Title)),
            palette));
        content.Children.Add(options);
        return RawCard(
            content,
            palette,
            [L(nameof(AppStrings.Settings_General_WindowManagement_SearchKeywords))]);
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
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Theme_SectionHeader)), palette);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_Theme_Header)),
            palette));
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_Theme_ThemeMode_Title)),
            L(nameof(AppStrings.Settings_Theme_ThemeMode_Description)),
            [
                (nameof(TrayAppDotNETThemeMode.System), L(nameof(AppStrings.Settings_Theme_ThemeMode_System))),
                (nameof(TrayAppDotNETThemeMode.Light), L(nameof(AppStrings.Settings_Theme_ThemeMode_Light))),
                (nameof(TrayAppDotNETThemeMode.Dark), L(nameof(AppStrings.Settings_Theme_ThemeMode_Dark)))
            ],
            _settings.ThemeMode.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayAppDotNETThemeMode value))
                    _settings.ThemeMode = value;
            },
            palette,
            () => RebuildShell(TaskManagerSettingsPage.Theme),
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_ThemeMode_SearchKeywords))],
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

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_Window_Header)),
            palette));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Theme_RoundedCorners_Title)),
            L(nameof(AppStrings.Settings_Theme_RoundedCorners_Description)),
            _settings.EnableRoundedCorners,
            value => _settings.EnableRoundedCorners = value,
            palette,
            () => RebuildShell(TaskManagerSettingsPage.Theme),
            [L(nameof(AppStrings.Settings_Theme_RoundedCorners_SearchKeywords))],
            ControlMap.Settings.ThemePage.RoundedCorners));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Theme_CollapseNavigationWhenNarrow_Title)),
            string.Format(
                CultureInfo.CurrentCulture,
                L(nameof(AppStrings.Settings_Theme_CollapseNavigationWhenNarrow_Description_Format)),
                _taskManagerResources.AxamlTaskManagerWindow.SidebarCollapseThreshold),
            _settings.CollapseSidebarWhenNarrow,
            value => _settings.CollapseSidebarWhenNarrow = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_CollapseNavigationWhenNarrow_SearchKeywords))],
            node: ControlMap.Settings.ThemePage.CollapseNavigationWhenNarrow));
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_Theme_Animations_Title)),
            L(nameof(AppStrings.Settings_Theme_Animations_Description)),
            [
                (nameof(TrayAppDotNETAnimationMode.System), L(nameof(AppStrings.Settings_Theme_Animations_System))),
                (nameof(TrayAppDotNETAnimationMode.Disabled), L(nameof(AppStrings.Settings_Theme_Animations_Disabled))),
                (nameof(TrayAppDotNETAnimationMode.Enabled), L(nameof(AppStrings.Settings_Theme_Animations_Enabled)))
            ],
            _settings.AnimationMode.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayAppDotNETAnimationMode value))
                    _settings.AnimationMode = value;
            },
            palette,
            ApplyAnimationMode,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_Animations_SearchKeywords))],
            node: ControlMap.Settings.ThemePage.Animations));

        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_Theme_ToolTipDelay_Title)),
            L(nameof(AppStrings.Settings_Theme_ToolTipDelay_Description)),
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
            suffix: L(nameof(AppStrings.Common_MillisecondsSuffix)),
            [L(nameof(AppStrings.Settings_Theme_ToolTipDelay_SearchKeywords))],
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
                Tagline = L(nameof(AppStrings.Settings_About_Tagline)),
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
            GetLanguage = () => _settings.Language,
            SetLanguage = language => _settings.Language = language,
            AvailableLanguages = AppLocalizationCultures.Names,
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
