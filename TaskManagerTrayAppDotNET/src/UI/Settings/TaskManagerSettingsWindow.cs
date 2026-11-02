using System.Globalization;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.Visuals;
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
public sealed partial class TaskManagerSettingsWindow : SettingsWindowCommon<TaskManagerSettingsPage>
{
    private const double PercentScale = 100;
    private const double GridTypographyStep = 0.5;
    private const double LiveTotalHorizontalScaleStepPercent = 5;
    private const string ShortcutGestureSeparator = "\n";

    private static string ResetText => L(nameof(AppStrings.Common_Reset));

    private static string DIPSuffix => L(nameof(AppStrings.Common_DIPSuffix));

    private static string PercentSuffix => L(nameof(CommonStrings.Common_PercentSuffix));

    private readonly AppSettings _settings;
    private readonly Func<InstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> _showUninstaller;
    private readonly TaskManagerWindowResources _taskManagerResources = TaskManagerWindowResources.Current;
    private readonly List<SettingsResettableNumber> _resettableNumbers = [];
    private SettingsButton? _resetPerformanceDeviceOrderButton;

    public TaskManagerSettingsWindow(
        AppSettings settings,
        Func<InstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> showUninstaller)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showUninstaller);

        _settings = settings;
        _showUninstaller = showUninstaller;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        ConfigureCompactSettingsWindow(title: L(nameof(AppStrings.TaskManager_SettingsWindow_Title)), icon: null);
        Topmost = settings.AlwaysOnTop;

        // A SettingsShell instance like the main window; tagged before the shell builds so its controls resolve
        this.MapTo(ControlMap.Settings.ID);
        InitializeSettingsShell();
#if DEBUG
        TaskManagerWindowResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
#endif
    }

    internal new void SelectPage(TaskManagerSettingsPage page) => base.SelectPage(page);

    protected override AppSettingsCommon CommonSettings => _settings;

    protected override AppTheme? SettingsTheme => AppServices.Theme;

    protected override TaskManagerSettingsPage DefaultPageKey => TaskManagerSettingsPage.General;

    protected override string HeaderText => L(nameof(AppStrings.TaskManager_SettingsWindow_Header));

    protected override IReadOnlyList<SettingsPageDescriptor<TaskManagerSettingsPage>> CreatePageDescriptors() =>
    [
        new(
            TaskManagerSettingsPage.General,
            Label: L(nameof(CommonStrings.Settings_Common_Page_General)),
            BuildGeneralPage,
            SettingsNavigationGlyphs.General),
        new(
            TaskManagerSettingsPage.Processes,
            Label: L(nameof(AppStrings.Settings_Common_Page_Processes)),
            BuildProcessesPage,
            TaskManagerGlyphCatalog.PROCESSES),
        new(
            TaskManagerSettingsPage.TrayIcon,
            Label: L(nameof(CommonStrings.Settings_Common_Page_TrayIcon)),
            BuildTrayIconPage,
            SettingsNavigationGlyphs.TrayIcon),
        new(
            TaskManagerSettingsPage.Performance,
            Label: L(nameof(AppStrings.Settings_Common_Page_Performance)),
            BuildPerformancePage,
            SettingsNavigationGlyphs.Devices),
        new(
            TaskManagerSettingsPage.Hotkeys,
            Label: L(nameof(CommonStrings.Settings_Common_Page_Hotkeys)),
            BuildHotkeysPage,
            SettingsNavigationGlyphs.Hotkeys),
        new(
            TaskManagerSettingsPage.Theme,
            Label: L(nameof(AppStrings.Settings_Page_Appearance)),
            BuildThemePage,
            SettingsNavigationGlyphs.Theme),
        new(
            TaskManagerSettingsPage.About,
            Label: L(nameof(CommonStrings.Settings_Common_Page_About)),
            BuildAboutPage,
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
}
