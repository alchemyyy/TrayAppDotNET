using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.ControlMapping;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

public sealed partial class TaskManagerSettingsWindow
{
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
            suffix: L(nameof(CommonStrings.Common_MillisecondsSuffix)),
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
}
