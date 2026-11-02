using System.Globalization;
using Avalonia.Controls;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Settings;
using TaskManagerTrayAppDotNET.Services;

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
        stack.Children.Add(BuildAlwaysRunAsAdministratorCard(palette));
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
            ElevatedSettingChangeResult result = await TaskManagerReplacement.SetEnabledElevatedAsync(requestedEnabled);
            TaskManagerReplacementState state = TaskManagerReplacement.GetState();
            bool installExists = TaskManagerReplacement.IsAvailable;
            toggle.IsEnabled = TaskManagerReplacement.CanToggle(installExists, state);

            bool applied = requestedEnabled
                ? result == ElevatedSettingChangeResult.Enabled
                : result == ElevatedSettingChangeResult.Disabled;
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
        ElevatedSettingChangeResult result,
        TaskManagerReplacementState state,
        bool systemInstallExists) => result switch
    {
        ElevatedSettingChangeResult.Declined => L(nameof(AppStrings.Settings_General_ElevatedChange_Declined)),
        ElevatedSettingChangeResult.Failed => L(nameof(AppStrings.Settings_General_ElevatedChange_Failed)),
        _ => DescribeReplacementState(state, systemInstallExists)
    };

    /// <summary>
    /// Builds the "Always run as administrator" card. Like the replacement card, it reflects the account's scheduled
    /// task rather than a stored setting and applies changes through an elevated relaunch, so the toggle reverts when
    /// administrator approval is declined or the change fails. Turning it on needs an administrator account running
    /// the Program Files system install; a registered task can be turned off from any copy.
    /// </summary>
    private Border BuildAlwaysRunAsAdministratorCard(SettingsPalette palette)
    {
        ElevatedStartState initialState = ElevatedStartTask.GetState();
        ElevatedStartAvailability availability = ElevatedStartTask.GetAvailability();
        bool suppressReentry = false;

        SettingsToggle toggle = new SettingsToggle(palette)
            {
                IsChecked = initialState.IsRegistered,
                IsEnabled = ElevatedStartTask.CanToggle(availability, initialState)
            }
            .MapTo(ControlMap.Settings.GeneralPage.AlwaysRunAsAdministrator);
        Border card = MutableCard(
            L(nameof(AppStrings.Settings_General_AlwaysRunAsAdministrator_Title)),
            DescribeElevatedStartState(initialState, availability),
            toggle,
            palette,
            out TextBlock descriptionText,
            searchKeywords: [L(nameof(AppStrings.Settings_General_AlwaysRunAsAdministrator_SearchKeywords))]);

        toggle.CheckedChanged += async (_, requestedEnabled) =>
        {
            if (suppressReentry) return;

            toggle.IsEnabled = false;
            ElevatedSettingChangeResult result = await ElevatedStartTask.SetEnabledElevatedAsync(requestedEnabled);
            ElevatedStartState state = ElevatedStartTask.GetState();
            toggle.IsEnabled = ElevatedStartTask.CanToggle(availability, state);

            // Show what Task Scheduler now holds, whatever the relaunch reported
            if (toggle.IsChecked != state.IsRegistered)
            {
                suppressReentry = true;
                toggle.IsChecked = state.IsRegistered;
                suppressReentry = false;
            }

            descriptionText.Text = result switch
            {
                ElevatedSettingChangeResult.Declined => L(nameof(AppStrings.Settings_General_ElevatedChange_Declined)),
                ElevatedSettingChangeResult.Failed => L(nameof(AppStrings.Settings_General_ElevatedChange_Failed)),
                _ => DescribeElevatedStartState(state, availability)
            };
        };

        return card;
    }

    private static string DescribeElevatedStartState(ElevatedStartState state, ElevatedStartAvailability availability)
    {
        if (state.IsRegistered)
        {
            string format = state.IsUsable
                ? L(nameof(AppStrings.Settings_General_AlwaysRunAsAdministrator_EnabledDescription_Format))
                : L(nameof(AppStrings.Settings_General_AlwaysRunAsAdministrator_NeedsRepairDescription_Format));
            return string.Format(CultureInfo.CurrentCulture, format, state.TaskPath);
        }
        if (!availability.AccountCanElevate)
            return L(nameof(AppStrings.Settings_General_AlwaysRunAsAdministrator_RequiresAdministrator));
        if (!availability.IsSystemInstall)
            return L(nameof(AppStrings.Settings_General_AlwaysRunAsAdministrator_RequiresSystemInstall));
        return L(nameof(AppStrings.Settings_General_AlwaysRunAsAdministrator_Description));
    }

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
