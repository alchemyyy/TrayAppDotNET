using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using BrightnessInstallScope = TrayAppDotNETCommon.Models.InstallScope;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private const double AutoEngageEnvironmentalCurveDelayBoxWidth = 96;
    private const double AutoEngageEnvironmentalCurveControlSpacing = 8;

    private readonly List<StackPanel> _profileSlotPanelGenerations = [];
    private readonly List<List<ProfileSlotEntry>> _profileSlotEntryGenerations = [];
    private List<ProfileSlotEntry> _profileSlots = [];
    private StackPanel? _profileSlotPanel;

    private StackPanel BuildGeneralPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_General_SectionHeader)), p);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(p);
        stack.Children.Add(commonSection.BuildStartupCard());
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_ApplyBrightnessOnStartup_Title)),
            L(nameof(AppStrings.Settings_General_ApplyBrightnessOnStartup_Description)),
            _settings.ApplyBrightnessOnStartup,
            v => _settings.ApplyBrightnessOnStartup = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_ApplyBrightnessOnStartup_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_Autosave_Title)),
            L(nameof(AppStrings.Settings_General_Autosave_Description)),
            _settings.Autosave,
            v => _settings.Autosave = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_Autosave_SearchKeywords))
            ]));
        stack.Children.Add(BuildAutoEngageEnvironmentalCurveCard(p));

        commonSection.AddInstallationSection(stack,
        [
            new TrayAppDotNETInstallCardOptions
            {
                Scope = BrightnessInstallScope.LocalAppData,
                Title = L(nameof(AppStrings.Settings_General_LocalUser_Title)),
                ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                Elevated = false,
                Install = static () => AppServices.Installation.InstallToLocalAppData(),
                UninstallAsync = refresh =>
                {
                    _showUninstaller(
                        AppServices.InstallLayout.LocalAppDataInstallDirectory,
                        BrightnessInstallScope.LocalAppData);
                    return Task.CompletedTask;
                }
            },
            new TrayAppDotNETInstallCardOptions
            {
                Scope = BrightnessInstallScope.ProgramFiles,
                Title = L(nameof(AppStrings.Settings_General_SystemWide_Title)),
                ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                Elevated = true,
                Install = static () => AppServices.Installation.InstallSystemWide(),
                UninstallAsync = refresh =>
                {
                    _showUninstaller(
                        AppServices.InstallLayout.ProgramFilesInstallDirectory,
                        BrightnessInstallScope.ProgramFiles);
                    return Task.CompletedTask;
                }
            }
        ]);
        CreateRenderingSettingsSection(p).AddCards(stack);

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(AppStrings.Settings_General_NightLight_Header)), p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_ShowNightLightSlider_Title)),
            L(nameof(AppStrings.Settings_General_ShowNightLightSlider_Description)),
            _settings.ShowNightLightSlider,
            v => _settings.ShowNightLightSlider = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_ShowNightLightSlider_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_InvertNightLightSlider_Title)),
            L(nameof(AppStrings.Settings_General_InvertNightLightSlider_Description)),
            _settings.InvertNightLightSlider,
            v => _settings.InvertNightLightSlider = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_InvertNightLightSlider_SearchKeywords))
            ]));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_General_TurnOffNightLightAtZero_Title)),
            L(nameof(AppStrings.Settings_General_TurnOffNightLightAtZero_Description)),
            _settings.TurnOffNightLightAtZeroStrength,
            v => _settings.TurnOffNightLightAtZeroStrength = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_TurnOffNightLightAtZero_SearchKeywords))
            ]));
        stack.Children.Add(StringComboCard(
            L(nameof(AppStrings.Settings_General_NightLightBackend_Title)),
            L(nameof(AppStrings.Settings_General_NightLightBackend_Description)),
            [
                (NightLightFallbackMode.SettingsHandler,
                    L(nameof(AppStrings.Settings_General_NightLightBackend_SettingsHandler))),
                (NightLightFallbackMode.Registry, L(nameof(AppStrings.Settings_General_NightLightBackend_Registry))),
                (NightLightFallbackMode.GammaRamp, L(nameof(AppStrings.Settings_General_NightLightBackend_GammaRamp)))
            ],
            _settings.NightLightFallbackMode,
            v => _settings.NightLightFallbackMode = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_NightLightBackend_SearchKeywords))
            ]));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_General_PDBTimeout_Title)),
            L(nameof(AppStrings.Settings_General_PDBTimeout_Description)),
            _settings.NightLightPDBDownloadTimeoutSeconds,
            1,
            600,
            v => _settings.NightLightPDBDownloadTimeoutSeconds = v,
            p,
            L(nameof(AppStrings.Common_SecondsSuffix)),
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_PDBTimeout_SearchKeywords))
            ]));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_General_EnvironmentalTick_Title)),
            L(nameof(AppStrings.Settings_General_EnvironmentalTick_Description)),
            _settings.EnvironmentalCurveTickIntervalMs,
            250,
            600_000,
            v => _settings.EnvironmentalCurveTickIntervalMs = v,
            p,
            Loc(nameof(AppStrings.Common_MillisecondsSuffix)),
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_EnvironmentalTick_SearchKeywords))
            ]));

        stack.Children.Add(
            TrayAppDotNETSettingsUI.SubsectionHeader(L(nameof(AppStrings.Settings_General_Profiles_Header)), p));
        StackPanel profileSlotPanel = new();
        List<ProfileSlotEntry> profileSlots = [];
        _profileSlotPanelGenerations.Add(profileSlotPanel);
        _profileSlotEntryGenerations.Add(profileSlots);
        _profileSlotPanel = profileSlotPanel;
        _profileSlots = profileSlots;
        AddPageCleanup(() =>
        {
            _profileSlotPanelGenerations.Remove(profileSlotPanel);
            _profileSlotEntryGenerations.Remove(profileSlots);
            if (ReferenceEquals(_profileSlotPanel, profileSlotPanel))
            {
                _profileSlotPanel = _profileSlotPanelGenerations.Count > 0
                    ? _profileSlotPanelGenerations[^1]
                    : null;
            }

            if (ReferenceEquals(_profileSlots, profileSlots))
            {
                _profileSlots = _profileSlotEntryGenerations.Count > 0
                    ? _profileSlotEntryGenerations[^1]
                    : [];
            }

            profileSlots.Clear();
        });
        RebuildProfileSlots();
        stack.Children.Add(RawCard(profileSlotPanel, p));

        return stack;
    }

    /// <summary>
    /// Builds the auto-engage card with its conditional seconds delay input.
    /// </summary>
    private Border BuildAutoEngageEnvironmentalCurveCard(SettingsPalette p)
    {
        SettingsNumberBox delayBox = TrayAppDotNETSettingsUI.NumberBox(
            p,
            _settings.AutoEngageEnvironmentalCurveDelaySeconds,
            TimeConstants.AutoEngageEnvironmentalCurveDelayMinSeconds,
            TimeConstants.AutoEngageEnvironmentalCurveDelayMaxSeconds,
            AutoEngageEnvironmentalCurveDelayBoxWidth,
            L(nameof(AppStrings.Common_SecondsSuffix)));
        delayBox.IsVisible = _settings.AutoEngageEnvironmentalCurveEnabled;
        delayBox.Margin = new Thickness(0, 0, AutoEngageEnvironmentalCurveControlSpacing, 0);
        delayBox.ValueChanged += (_, e) =>
        {
            if (!e.NewValue.HasValue) return;
            _settings.AutoEngageEnvironmentalCurveDelaySeconds = (int)e.NewValue.Value;
            Save();
        };

        SettingsToggle toggle = TrayAppDotNETSettingsUI.Toggle(
            p,
            _settings.AutoEngageEnvironmentalCurveEnabled,
            (_, enabled) =>
            {
                _settings.AutoEngageEnvironmentalCurveEnabled = enabled;
                delayBox.IsVisible = enabled;
                Save();
            });

        StackPanel controls = TrayAppDotNETSettingsUI.Horizontal(delayBox, toggle);
        return Card(
            L(nameof(AppStrings.Settings_General_AutoEngageEnvironmentalCurve_Title)),
            L(nameof(AppStrings.Settings_General_AutoEngageEnvironmentalCurve_Description)),
            controls,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_General_AutoEngageEnvironmentalCurve_SearchKeywords))
            ]);
    }

    private void RebuildProfileSlots()
    {
        if (_profileSlotPanel == null) return;
        _profileSlotPanel.Children.Clear();
        _profileSlots.Clear();
        if (_profileManager == null)
        {
            TextBlock unavailable = ControlNames.Assign(
                TrayAppDotNETSettingsUI.DescriptionText(
                    L(nameof(AppStrings.Settings_General_ProfileManagerUnavailable)),
                    Palette),
                "ProfileSlots");
            _profileSlotPanel.Children.Add(unavailable);
            return;
        }

        for (int i = 0; i < _profileManager.Profiles.Profiles.Count; i++)
        {
            string defaultName = L(nameof(AppStrings.Settings_General_DefaultProfileName));
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
        SettingsButton up = Button(GlyphCatalog.CHEVRON_UP.Text, p);
        SettingsButton down = Button(GlyphCatalog.CHEVRON_DOWN.Text, p);
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
        ControlNames.AssignLogicalSubtree(row, "ProfileSlot");
        return row;
    }

    private void CommitProfileName(ProfileSlotEntry entry, string? text)
    {
        if (_profileManager == null) return;
        string trimmed = (text ?? string.Empty).Trim();
        string defaultName = L(nameof(AppStrings.Settings_General_DefaultProfileName));
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

    private TrayAppDotNETRenderingSettingsSection CreateRenderingSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETRenderingSettingsSectionOptions
        {
            Palette = p,
            CardRadius = RadiusLarge,
            L = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            RenderingSettings = _settings,
            WarmWindowSettings = _settings,
            SupportsFlyoutWarmWindow = true,
            SupportsTrayContextMenuWarmWindow = true
        });

    private sealed record ProfileSlotEntry(int Key, string Name);
}
