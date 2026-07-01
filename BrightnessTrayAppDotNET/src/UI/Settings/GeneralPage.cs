using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using BrightnessInstallScope = TrayAppDotNETCommon.InstallScope;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private readonly List<ProfileSlotEntry> _profileSlots = [];
    private StackPanel? _profileSlotPanel;

    private StackPanel BuildGeneralPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_General_SectionHeader", "General"), p);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(p);
        stack.Children.Add(commonSection.BuildStartupCard());

        commonSection.AddInstallationSection(stack,
        [
            new TrayAppDotNETInstallCardOptions
            {
                Scope = BrightnessInstallScope.LocalAppData,
                Title = L("Settings_General_LocalUser_Title", "Local user"),
                ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                Elevated = false,
                Install = static () => AppServices.Installation.InstallToLocalAppData(),
                UninstallAsync = refresh =>
                {
                    _showUninstaller(
                        AppServices.InstallLayout.LocalAppDataInstallDirectory,
                        BrightnessInstallScope.LocalAppData);
                    return Task.CompletedTask;
                },
            },
            new TrayAppDotNETInstallCardOptions
            {
                Scope = BrightnessInstallScope.ProgramFiles,
                Title = L("Settings_General_SystemWide_Title", "System-wide"),
                ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                Elevated = true,
                Install = static () => AppServices.Installation.InstallSystemWide(),
                UninstallAsync = refresh =>
                {
                    _showUninstaller(
                        AppServices.InstallLayout.ProgramFilesInstallDirectory,
                        BrightnessInstallScope.ProgramFiles);
                    return Task.CompletedTask;
                },
            },
        ]);
        CreateKeepWarmSettingsSection(p).AddCards(stack);

        stack.Children.Add(BoolCard(
            L("Settings_General_ApplyBrightnessOnStartup_Title", "Apply brightness on startup"),
            L("Settings_General_ApplyBrightnessOnStartup_Description",
                "Restore the selected profile's saved brightness values when the app starts."),
            _settings.ApplyBrightnessOnStartup,
            v => _settings.ApplyBrightnessOnStartup = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_General_Autosave_Title", "Autosave profiles"),
            L("Settings_General_Autosave_Description",
                "Save profile changes automatically after brightness or monitor-state edits."),
            _settings.Autosave,
            v => _settings.Autosave = v,
            p));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L("Settings_General_NightLight_Header", "Night light"), p));
        stack.Children.Add(BoolCard(
            L("Settings_General_ShowNightLightSlider_Title", "Show night-light slider"),
            L("Settings_General_ShowNightLightSlider_Description",
                "Include the Windows Night Light strength slider in the flyout."),
            _settings.ShowNightLightSlider,
            v => _settings.ShowNightLightSlider = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_General_InvertNightLightSlider_Title", "Invert night-light slider"),
            L("Settings_General_InvertNightLightSlider_Description",
                "Make higher slider positions cooler instead of warmer."),
            _settings.InvertNightLightSlider,
            v => _settings.InvertNightLightSlider = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_General_TurnOffNightLightAtZero_Title", "Turn off at zero strength"),
            L("Settings_General_TurnOffNightLightAtZero_Description",
                "Dragging night light to 0 also disables Night Light."),
            _settings.TurnOffNightLightAtZeroStrength,
            v => _settings.TurnOffNightLightAtZeroStrength = v,
            p));
        stack.Children.Add(StringComboCard(
            L("Settings_General_NightLightBackend_Title", "Night-light backend"),
            L("Settings_General_NightLightBackend_Description",
                "Choose how the app applies Windows Night Light changes."),
            [
                (NightLightFallbackMode.SettingsHandler,
                    L("Settings_General_NightLightBackend_SettingsHandler", "Settings handler")),
                (NightLightFallbackMode.Registry, L("Settings_General_NightLightBackend_Registry", "Registry")),
                (NightLightFallbackMode.GammaRamp, L("Settings_General_NightLightBackend_GammaRamp", "Gamma ramp")),
            ],
            _settings.NightLightFallbackMode,
            v => _settings.NightLightFallbackMode = v,
            p));
        stack.Children.Add(IntCard(
            L("Settings_General_PDBTimeout_Title", "PDB download timeout"),
            L("Settings_General_PDBTimeout_Description",
                "Maximum seconds to wait for SettingsHandlers_Display.dll symbols."),
            _settings.NightLightPDBDownloadTimeoutSeconds,
            1,
            600,
            v => _settings.NightLightPDBDownloadTimeoutSeconds = v,
            p,
            L("Common_SecondsSuffix", "s")));
        stack.Children.Add(IntCard(
            L("Settings_General_EnvironmentalTick_Title", "Environmental tick interval"),
            L("Settings_General_EnvironmentalTick_Description", "Milliseconds between active curve evaluations."),
            _settings.EnvironmentalCurveTickIntervalMs,
            250,
            600_000,
            v => _settings.EnvironmentalCurveTickIntervalMs = v,
            p,
            Loc("Common_MillisecondsSuffix")));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L("Settings_General_Profiles_Header", "Profiles"), p));
        _profileSlotPanel = new StackPanel();
        RebuildProfileSlots();
        stack.Children.Add(RawCard(_profileSlotPanel, p));

        return stack;
    }

    private void RebuildProfileSlots()
    {
        if (_profileSlotPanel == null) return;
        _profileSlotPanel.Children.Clear();
        _profileSlots.Clear();
        if (_profileManager == null)
        {
            _profileSlotPanel.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
                L("Settings_General_ProfileManagerUnavailable", "Profile manager is not available."), Palette));
            return;
        }

        for (int i = 0; i < _profileManager.Profiles.Profiles.Count; i++)
        {
            string defaultName = L("Settings_General_DefaultProfileName", "Profile");
            string name = string.IsNullOrWhiteSpace(_profileManager.Profiles.Profiles[i].Name)
                ? defaultName
                : _profileManager.Profiles.Profiles[i].Name!;
            _profileSlots.Add(new ProfileSlotEntry(i, name));
        }

        for (int i = 0; i < _profileSlots.Count; i++) _profileSlotPanel.Children.Add(BuildProfileSlotRow(i));
    }

    private Grid BuildProfileSlotRow(int index)
    {
        SettingsPalette p = Palette;
        ProfileSlotEntry entry = _profileSlots[index];
        TextBlock label = TrayAppDotNETSettingsUI.TitleText((index + 1).ToString(CultureInfo.InvariantCulture), p);
        label.Width = 28;
        label.VerticalAlignment = VerticalAlignment.Center;
        TextBox nameBox = TrayAppDotNETSettingsUI.TextBox(p, 220, entry.Name);
        nameBox.LostFocus += (_, _) => CommitProfileName(entry, nameBox.Text);
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitProfileName(entry, nameBox.Text);
            e.Handled = true;
        };
        SettingsButton up = Button(GlyphCatalog.CHEVRON_UP, p);
        SettingsButton down = Button(GlyphCatalog.CHEVRON_DOWN, p);
        up.Width = 32;
        down.Width = 32;
        up.Padding = new Thickness(0);
        down.Padding = new Thickness(0);
        up.Label.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        down.Label.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        up.IsEnabled = index > 0;
        down.IsEnabled = index < _profileSlots.Count - 1;
        up.Click += (_, _) => MoveProfileSlot(index, -1);
        down.Click += (_, _) => MoveProfileSlot(index, 1);
        Grid row = new() { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.Children.Add(label);
        Grid.SetColumn(nameBox, 1);
        row.Children.Add(nameBox);
        StackPanel buttons = TrayAppDotNETSettingsUI.Horizontal(up, down);
        buttons.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(buttons, 3);
        row.Children.Add(buttons);
        return row;
    }

    private void CommitProfileName(ProfileSlotEntry entry, string? text)
    {
        if (_profileManager == null) return;
        string trimmed = (text ?? string.Empty).Trim();
        string defaultName = L("Settings_General_DefaultProfileName", "Profile");
        string? stored =
            string.IsNullOrWhiteSpace(trimmed) || string.Equals(trimmed, defaultName, StringComparison.CurrentCulture)
                ? null
                : trimmed;
        _profileManager.RenameProfile(entry.Key, stored);
        _profileManager.RaiseProfilesListChanged();
    }

    private void MoveProfileSlot(int index, int delta)
    {
        if (_profileManager == null) return;
        int target = index + delta;
        if (target < 0 || target >= _profileSlots.Count) return;
        (_profileSlots[index], _profileSlots[target]) = (_profileSlots[target], _profileSlots[index]);
        _profileManager.SwapProfileData([.. _profileSlots.Select(static s => s.Key)]);
        _profileManager.RaiseProfilesListChanged();
        RebuildProfileSlots();
    }

    private TrayAppDotNETGeneralSettingsSection CreateGeneralSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETGeneralSettingsSectionOptions
        {
            Palette = p,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            Localize = L,
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
            CurrentBuildNumber = BuildInfo.BuildNumber,
        });

    private TrayAppDotNETKeepWarmSettingsSection CreateKeepWarmSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETKeepWarmSettingsSectionOptions
        {
            Palette = p,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            Settings = _settings,
            SupportsFlyout = true,
            SupportsTrayContextMenu = true,
        });

    private sealed record ProfileSlotEntry(int Key, string Name);
}
