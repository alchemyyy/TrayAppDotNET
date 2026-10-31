using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace TaskManagerTrayAppDotNET.UI.ProcessesPage;

/// <summary>Configures Processes-column visibility and left-to-right ordering.</summary>
internal sealed class ProcessColumnChooserWindow : TaskManagerReorderDialog<ProcessColumnSetting>
{
    private readonly AppSettings _settings;
    private readonly CheckBox _hideUnusedColumns;
#if DEBUG
    private readonly List<ProcessColumnSetting> _items;
#endif

    public ProcessColumnChooserWindow(
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        bool isLightTheme,
        Action<IReadOnlyList<ProcessColumnSetting>> columnsChanged)
        : this(
            ProcessColumnSettings.CloneList(
                (settings ?? throw new ArgumentNullException(nameof(settings))).DetailsColumns),
            settings,
            palette ?? throw new ArgumentNullException(nameof(palette)),
            resources ?? throw new ArgumentNullException(nameof(resources)),
            isLightTheme,
            ResolveBackground(resources, isLightTheme),
            CreateHideUnusedColumnsCheckBox(palette, settings.HideUnusedProcessColumns),
            columnsChanged)
    {
    }

    private ProcessColumnChooserWindow(
        List<ProcessColumnSetting> items,
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        bool isLightTheme,
        Color background,
        CheckBox hideUnusedColumns,
        Action<IReadOnlyList<ProcessColumnSetting>> columnsChanged)
        : base(
            title: LocalizationManager.Instance[nameof(AppStrings.Columns_Chooser_Title)],
            description: LocalizationManager.Instance[nameof(AppStrings.Columns_Chooser_Description)],
            items,
            GetSearchText,
            (setting, itemChanged) => BuildVisibilityCheckBox(
                setting,
                items,
                palette,
                resources,
                itemChanged),
#if DEBUG
            () => CreateDefaultItems(resources),
#else
            ProcessColumnSettings.CreateDefault,
#endif
            orderedItems => columnsChanged(ProcessColumnSettings.CloneList(orderedItems)),
            palette,
            settings.EnableRoundedCorners,
            resources,
            background,
            resources.AxamlTaskManagerReorderDialog.ColumnWindowWidth,
            resources.AxamlTaskManagerReorderDialog.ColumnWindowHeight,
            resources.AxamlTaskManagerReorderDialog.ColumnWindowMinHeight,
            showSearch: true,
            searchPlaceholder: LocalizationManager.Instance[nameof(AppStrings.Columns_Chooser_SearchPlaceholder)],
            CreateScrollBarStyle(resources, background, isLightTheme),
            TaskManagerContextMenuWindow.CreateOptions(
                palette,
                settings.EnableRoundedCorners,
                settings),
            setting => ToggleVisibility(items, setting),
            hideUnusedColumns)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(columnsChanged);

        // A ReorderDialog instance; the surface id tells it from the header button arrangement dialog
        this.MapTo(ControlMap.ProcessColumnChooser.ID);
        _settings = settings;
        _hideUnusedColumns = hideUnusedColumns;
#if DEBUG
        _items = items;
#endif
        if (_hideUnusedColumns.IsChecked == true) ApplyHideUnusedColumnsFilter();
        _hideUnusedColumns.IsCheckedChanged += OnHideUnusedColumnsChanged;
        Closed += OnChooserClosed;
    }

#if DEBUG
    // AXAML hot-reload exception: The generic reorder dialog keeps its mutable unsaved rows in
    // private visual state, so rebuilding its chrome would discard the active edit
    /// <summary>Applies safe window metrics and authoritative widths while retaining unsaved row state.</summary>
    internal void ApplyAXAMLResources(
        TaskManagerWindowResources resources,
        IReadOnlyList<ProcessColumnSetting> currentSettings)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(currentSettings);

        Width = resources.AxamlTaskManagerReorderDialog.ColumnWindowWidth;
        MinWidth = Width;
        MaxWidth = Width;
        Height = resources.AxamlTaskManagerReorderDialog.ColumnWindowHeight;
        MinHeight = resources.AxamlTaskManagerReorderDialog.ColumnWindowMinHeight;
        MaxHeight = Height;

        for (int itemIndex = 0; itemIndex < _items.Count; itemIndex++)
        {
            ProcessColumnSetting item = _items[itemIndex];
            for (int settingIndex = 0; settingIndex < currentSettings.Count; settingIndex++)
            {
                ProcessColumnSetting currentSetting = currentSettings[settingIndex];
                if (currentSetting.Column != item.Column) continue;

                item.Width = currentSetting.Width;
                break;
            }
        }
    }
#endif

    private static CheckBox CreateHideUnusedColumnsCheckBox(SettingsPalette palette, bool isChecked) => new CheckBox
    {
        Content = LocalizationManager.Instance[nameof(AppStrings.Columns_Chooser_HideUnused)],
        Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
        IsChecked = isChecked,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center
    }.MapTo(ControlMap.ProcessColumnChooser.HideUnusedColumns);

    private void OnHideUnusedColumnsChanged(object? sender, RoutedEventArgs eventArgs)
    {
        ApplyHideUnusedColumnsFilter();
        _settings.UpdateHideUnusedProcessColumns(_hideUnusedColumns.IsChecked == true);
    }

    private void ApplyHideUnusedColumnsFilter()
    {
        Func<ProcessColumnSetting, bool>? includeItem = _hideUnusedColumns.IsChecked == true
            ? static setting => setting.Visible
            : null;
        SetItemFilter(includeItem);
    }

    private void OnChooserClosed(object? sender, EventArgs eventArgs)
    {
        Closed -= OnChooserClosed;
        _hideUnusedColumns.IsCheckedChanged -= OnHideUnusedColumnsChanged;
    }

    private static string GetSearchText(ProcessColumnSetting setting)
    {
        ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
        return string.IsNullOrWhiteSpace(setting.Nickname)
            ? definition.Title
            : definition.Title + " " + setting.Nickname;
    }

    private static Grid BuildVisibilityCheckBox(
        ProcessColumnSetting setting,
        IReadOnlyList<ProcessColumnSetting> settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Action itemChanged)
    {
        ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
        CheckBox visibility = new CheckBox
        {
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            IsChecked = setting.Visible,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        }.MapTo(ControlMap.ProcessColumnChooser.Visible);
        visibility.IsCheckedChanged += (_, _) =>
        {
            bool isVisible = visibility.IsChecked == true;
            if (setting.Visible == isVisible) return;
            if (!isVisible && !HasOtherVisibleColumn(settings, setting))
            {
                visibility.IsChecked = true;
                return;
            }

            setting.Visible = isVisible;
            itemChanged();
        };

        TextBlock label = TrayAppDotNETSettingsUI.Text(definition.Title, palette);
        label.IsHitTestVisible = false;
        label.Margin = resources.AxamlTaskManagerReorderDialog.CheckBoxLabelMargin;
        label.VerticalAlignment = VerticalAlignment.Center;

        Grid content = new()
        {
            Background = Brushes.Transparent,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
        };
        content.Children.Add(visibility);
        Grid.SetColumn(label, value: 1);
        content.Children.Add(label);
        return content;
    }

    private static void ToggleVisibility(
        IReadOnlyList<ProcessColumnSetting> settings,
        ProcessColumnSetting setting)
    {
        if (setting.Visible && !HasOtherVisibleColumn(settings, setting)) return;
        setting.Visible = !setting.Visible;
    }

#if DEBUG
    private static List<ProcessColumnSetting> CreateDefaultItems(
        TaskManagerWindowResources resources)
    {
        List<ProcessColumnSetting> defaults = ProcessColumnSettings.CreateDefault();
        ProcessTableAXAMLColumnWidths currentWidths = new(
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.Name).DefaultWidth,
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.ProcessID).DefaultWidth,
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.Status).DefaultWidth,
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.UserName).DefaultWidth,
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.CPU).DefaultWidth,
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.Lifetime).DefaultWidth,
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.PrivateMemory).DefaultWidth,
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.SharedWorkingSet).DefaultWidth,
            ProcessTableColumnCatalog.Get(ProcessTableColumnKind.CommandLine).DefaultWidth);
        ProcessTableAXAMLColumnWidths nextWidths = new(
            resources.AxamlProcessTable.NameColumnWidth,
            resources.AxamlProcessTable.PIDColumnWidth,
            resources.AxamlProcessTable.StatusColumnWidth,
            resources.AxamlProcessTable.UserNameColumnWidth,
            resources.AxamlProcessTable.CPUColumnWidth,
            resources.AxamlProcessTable.LifetimeColumnWidth,
            resources.AxamlProcessTable.PrivateMemoryColumnWidth,
            resources.AxamlProcessTable.WorkingSetColumnWidth,
            resources.AxamlProcessTable.CommandLineColumnWidth);
        _ = ProcessTableAXAMLHotReload.TryApplyColumnWidths(
            defaults,
            currentWidths,
            nextWidths,
            out defaults);
        return defaults;
    }
#endif

    private static bool HasOtherVisibleColumn(
        IReadOnlyList<ProcessColumnSetting> settings,
        ProcessColumnSetting excludedSetting)
    {
        for (int settingIndex = 0; settingIndex < settings.Count; settingIndex++)
        {
            ProcessColumnSetting candidate = settings[settingIndex];
            if (!ReferenceEquals(candidate, excludedSetting) && candidate.Visible) return true;
        }

        return false;
    }

    /// <summary>Asks before restoring every column property to its default value.</summary>
    protected override async Task<bool> ConfirmResetAsync()
    {
        using TrayAppDotNETConfirmationWindow confirmation = new(
            title: LocalizationManager.Instance[nameof(AppStrings.Columns_Reset_Title)],
            description: LocalizationManager.Instance[nameof(AppStrings.Columns_Reset_Message)],
            confirmText: LocalizationManager.Instance[nameof(AppStrings.Common_Reset)],
            Palette,
            RoundedCornersEnabled,
            cancelText: LocalizationManager.Instance[nameof(CommonStrings.Common_Cancel)]) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        confirmation.MapTo(ControlMap.ProcessColumnResetConfirmation.ID);
        TrayAppDotNETConfirmationResult result =
            await confirmation.ShowDialog<TrayAppDotNETConfirmationResult>(this);
        return result == TrayAppDotNETConfirmationResult.Confirmed;
    }

    private static Color ResolveBackground(TaskManagerWindowResources resources, bool isLightTheme) =>
        isLightTheme
            ? resources.AxamlProcessColumnChooser.LightBackgroundColor
            : resources.AxamlProcessColumnChooser.DarkBackgroundColor;

    private static SettingsScrollBarStyle CreateScrollBarStyle(
        TaskManagerWindowResources resources,
        Color background,
        bool isLightTheme) =>
        new(
            resources.AxamlProcessTable.ScrollBarTrackThickness,
            resources.AxamlProcessTable.ScrollBarIdleThumbThickness,
            resources.AxamlProcessTable.ScrollBarHoverThumbThickness,
            resources.AxamlProcessTable.ScrollBarThumbEndMargin,
            resources.AxamlProcessTable.ScrollBarMinimumThumbLength,
            background,
            resources.AxamlProcessTable.ScrollThumbColor,
            resources.ScrollHoverThumbColor(isLightTheme),
            resources.ScrollHoverThumbColor(isLightTheme),
            resources.ScrollHoverThumbColor(isLightTheme),
            ShowButtonsOnHover: true);
}
