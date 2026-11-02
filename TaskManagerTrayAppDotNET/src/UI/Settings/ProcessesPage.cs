using Avalonia.Controls;

namespace TaskManagerTrayAppDotNET.UI;

public sealed partial class TaskManagerSettingsWindow
{
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
}
