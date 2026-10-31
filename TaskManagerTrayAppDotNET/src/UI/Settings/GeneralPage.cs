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

public sealed partial class TaskManagerSettingsWindow
{
    private StackPanel BuildGeneralPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_General_SectionHeader)), palette);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(
            AppServices.Install,
            AppLocalizationCultures.Names,
            () => _settings.StartMinimized,
            value => _settings.StartMinimized = value);
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

        AddStandardInstallationSection(stack, commonSection, AppServices.Install, _showUninstaller, includeStoreCard: true);
        // Task Manager's process row menu is the only tray-app menu with submenus
        AddRenderingSection(
            stack,
            supportsFlyoutWarmWindow: false,
            supportsTrayContextMenuWarmWindow: false,
            supportsSubmenus: true);
        return stack;
    }

    /// <summary>
    /// Builds the system-wide "Replace Windows Task Manager" card. Unlike a plain setting, this reflects
    /// the live registry redirect and applies changes through an elevated relaunch, so the toggle reverts
    /// when administrator approval is declined or the write fails. The redirect may only target the Program
    /// Files system install, so without one the card is disabled and says why (a redirect this copy already
    /// owns can still be turned off).
    /// </summary>
    private Border BuildReplaceTaskManagerCard(SettingsPalette palette)
    {
        TaskManagerReplacementState initialState = TaskManagerReplacement.GetState();
        bool systemInstallExists = TaskManagerReplacement.IsAvailable;
        bool suppressReentry = false;

        SettingsToggle toggle = new SettingsToggle(palette)
            {
                IsChecked = initialState.IsEnabled,
                IsEnabled = TaskManagerReplacement.CanToggle(systemInstallExists, initialState)
            }
            .MapTo(ControlMap.Settings.GeneralPage.ReplaceWindowsTaskManager);
        Border card = MutableCard(
            L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_Title)),
            DescribeReplacementState(initialState, systemInstallExists),
            toggle,
            palette,
            out TextBlock descriptionText,
            searchKeywords: [L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_SearchKeywords))]);

        toggle.CheckedChanged += async (_, requestedEnabled) =>
        {
            if (suppressReentry) return;

            toggle.IsEnabled = false;
            TaskManagerReplacementResult result = await TaskManagerReplacement.SetEnabledElevatedAsync(requestedEnabled);
            TaskManagerReplacementState state = TaskManagerReplacement.GetState();
            bool installExists = TaskManagerReplacement.IsAvailable;
            toggle.IsEnabled = TaskManagerReplacement.CanToggle(installExists, state);

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

            descriptionText.Text = DescribeReplacementResult(result, state, installExists);
        };

        return card;
    }

    private static string DescribeReplacementState(TaskManagerReplacementState state, bool systemInstallExists)
    {
        if (state.IsEnabled)
        {
            return systemInstallExists
                ? L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_EnabledDescription))
                : L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_EnabledWithoutSystemInstall));
        }
        if (!systemInstallExists)
            return L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_RequiresSystemInstall));
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

    private static string DescribeReplacementResult(
        TaskManagerReplacementResult result,
        TaskManagerReplacementState state,
        bool systemInstallExists) => result switch
    {
        TaskManagerReplacementResult.Declined =>
            L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_Declined)),
        TaskManagerReplacementResult.Failed => L(nameof(AppStrings.Settings_General_ReplaceWindowsTaskManager_Failed)),
        _ => DescribeReplacementState(state, systemInstallExists)
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
}
