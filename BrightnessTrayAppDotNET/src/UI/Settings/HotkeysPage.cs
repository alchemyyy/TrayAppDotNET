using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using BrightnessHotkeyBinding = BrightnessTrayAppDotNET.Models.HotkeyBinding;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildHotkeysPage() => BuildHotkeysPage(
        _settings,
        static () => AppServices.HotkeyService,
        HotkeyRows(),
        AddMonitorOffBindingButton);

    private List<TrayAppDotNETHotkeyRow<BrightnessHotkeyAction>> HotkeyRows()
    {
        List<TrayAppDotNETHotkeyRow<BrightnessHotkeyAction>> rows =
        [
            Row(BrightnessHotkeyAction.OpenFlyout,
                nameof(CommonStrings.Settings_Hotkeys_OpenFlyout_Title),
                nameof(AppStrings.Settings_Hotkeys_OpenFlyout_Description)),
            Row(BrightnessHotkeyAction.OpenSettings,
                nameof(CommonStrings.Settings_Hotkeys_OpenSettings_Title),
                nameof(CommonStrings.Settings_Hotkeys_OpenSettings_Description)),
            Row(BrightnessHotkeyAction.FullBright,
                nameof(AppStrings.Settings_Hotkeys_FullBright_Title),
                nameof(AppStrings.Settings_Hotkeys_FullBright_Description)),
            Row(BrightnessHotkeyAction.FullDim,
                nameof(AppStrings.Settings_Hotkeys_FullDim_Title),
                nameof(AppStrings.Settings_Hotkeys_FullDim_Description)),
            Row(BrightnessHotkeyAction.IncrementMasterBrightness,
                nameof(AppStrings.Settings_Hotkeys_IncrementMasterBrightness_Title),
                nameof(AppStrings.Settings_Hotkeys_IncrementMasterBrightness_Description)),
            Row(BrightnessHotkeyAction.DecrementMasterBrightness,
                nameof(AppStrings.Settings_Hotkeys_DecrementMasterBrightness_Title),
                nameof(AppStrings.Settings_Hotkeys_DecrementMasterBrightness_Description)),
            Row(BrightnessHotkeyAction.NormalizeBrightnesses,
                nameof(AppStrings.Settings_Hotkeys_NormalizeBrightnesses_Title),
                nameof(AppStrings.Settings_Hotkeys_NormalizeBrightnesses_Description)),
            Row(BrightnessHotkeyAction.ToggleNightLight,
                nameof(AppStrings.Settings_Hotkeys_ToggleNightLight_Title),
                nameof(AppStrings.Settings_Hotkeys_ToggleNightLight_Description)),
            Row(BrightnessHotkeyAction.IncrementNightLight,
                nameof(AppStrings.Settings_Hotkeys_IncrementNightLight_Title),
                nameof(AppStrings.Settings_Hotkeys_IncrementNightLight_Description)),
            Row(BrightnessHotkeyAction.DecrementNightLight,
                nameof(AppStrings.Settings_Hotkeys_DecrementNightLight_Title),
                nameof(AppStrings.Settings_Hotkeys_DecrementNightLight_Description)),
            Row(BrightnessHotkeyAction.PowerOffAllMonitors,
                nameof(AppStrings.Settings_Hotkeys_PowerOffAllMonitors_Title),
                nameof(AppStrings.Settings_Hotkeys_PowerOffAllMonitors_Description))
        ];

        if (_profileManager != null)
        {
            for (int i = 0; i < _profileManager.Profiles.Profiles.Count; i++)
            {
                string name = _profileManager.GetName(i) is { Length: > 0 } profileName
                    ? profileName
                    : string.Format(CultureInfo.CurrentCulture,
                        L(nameof(AppStrings.Settings_Hotkeys_DefaultProfileName_Format)), i + 1);
                rows.Add(new TrayAppDotNETHotkeyRow<BrightnessHotkeyAction>
                {
                    Action = BrightnessHotkeyAction.ProfileSelect,
                    Parameter = i.ToString(CultureInfo.InvariantCulture),
                    Title = string.Format(CultureInfo.CurrentCulture,
                        L(nameof(AppStrings.Settings_Hotkeys_SelectProfile_Title_Format)), name),
                    Description = L(nameof(AppStrings.Settings_Hotkeys_SelectProfile_Description))
                });
            }
        }

        // Display power-off rows carry their target picker and remove button in the row's extension slots
        foreach (string parameter in ExistingMonitorOffParameters())
        {
            rows.Add(new TrayAppDotNETHotkeyRow<BrightnessHotkeyAction>
            {
                Action = BrightnessHotkeyAction.MonitorOff,
                Parameter = parameter,
                Title = L(nameof(AppStrings.Settings_Hotkeys_PowerOffSpecificMonitor_Title)),
                Description = MonitorTargetLabel(parameter),
                BuildExtras = BuildMonitorTargetExtras
            });
        }

        return rows;

        TrayAppDotNETHotkeyRow<BrightnessHotkeyAction> Row(
            BrightnessHotkeyAction action,
            string titleKey,
            string descriptionKey) =>
            new() { Action = action, Title = L(titleKey), Description = L(descriptionKey) };
    }

    private TrayAppDotNETHotkeyRowExtras BuildMonitorTargetExtras(TrayAppDotNETHotkeyRowContext row)
    {
        SettingsPalette p = Palette;
        SettingsComboBox targetCombo = TrayAppDotNETSettingsUI.ComboBox(p, width: 240)
            .MapTo(ControlMap.Settings.HotkeysPage.Hotkeys.MonitorTarget);
        targetCombo.Margin = new Avalonia.Thickness(left: 0, top: 0, right: 8, bottom: 0);
        IReadOnlyList<(string Value, string Label)> targets = BuildMonitorTargetOptions();
        foreach ((string value, string label) in targets)
            targetCombo.Items.Add(new SettingsComboBoxItem(value, label, p));
        if (!string.IsNullOrWhiteSpace(row.Parameter)
            && !targets.Any(t => string.Equals(t.Value, row.Parameter, StringComparison.Ordinal)))
            targetCombo.Items.Add(new SettingsComboBoxItem(row.Parameter, MonitorTargetLabel(row.Parameter), p));
        TrayAppDotNETSettingsUI.SelectComboByTag(targetCombo, row.Parameter);
        targetCombo.SelectionChanged += (_, _) =>
        {
            string newParameter = TrayAppDotNETSettingsUI.SelectedTag(targetCombo) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newParameter)
                || string.Equals(newParameter, row.Parameter, StringComparison.Ordinal))
                return;

            // A target already used by another power-off row keeps its own row
            if (ExistingMonitorOffParameters().Contains(newParameter, StringComparer.Ordinal))
            {
                TrayAppDotNETSettingsUI.SelectComboByTag(targetCombo, row.Parameter);
                return;
            }

            row.ChangeParameter(newParameter);
        };

        SettingsButton removeTarget = Button(L(nameof(AppStrings.Settings_Hotkeys_Remove_Button)), p)
            .MapTo(ControlMap.Settings.HotkeysPage.Hotkeys.RemoveMonitorTarget);
        removeTarget.Click += (_, _) =>
        {
            string parameter = row.Parameter;
            _settings.Hotkeys.RemoveAll(b => b.Matches(BrightnessHotkeyAction.MonitorOff, parameter));
            Save();
            RebuildShell(BrightnessSettingsPage.Hotkeys);
        };

        return new TrayAppDotNETHotkeyRowExtras(targetCombo, removeTarget);
    }

    private void AddMonitorOffBindingButton(
        StackPanel stack,
        TrayAppDotNETHotkeysSection<BrightnessHotkeyAction, BrightnessHotkeyBinding> section)
    {
        IReadOnlyList<(string Value, string Label)> targets = BuildMonitorTargetOptions();
        SettingsButton add = Button(L(nameof(AppStrings.Settings_Hotkeys_AddMonitorOffBinding_Button)), Palette)
            .MapTo(ControlMap.Settings.HotkeysPage.Hotkeys.AddMonitorOffBinding);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Avalonia.Thickness(left: 0, top: 8, right: 0, bottom: 0);
        add.IsEnabled = targets.Any(t => !ExistingMonitorOffParameters().Contains(t.Value, StringComparer.Ordinal));
        add.Click += (_, _) =>
        {
            string? parameter = targets
                .Select(static t => t.Value)
                .FirstOrDefault(v => !ExistingMonitorOffParameters().Contains(v, StringComparer.Ordinal));
            if (string.IsNullOrWhiteSpace(parameter)) return;

            // An unbound row: the target is chosen, the key combination is added on the new card
            _settings.Hotkeys.Add(new BrightnessHotkeyBinding
            {
                Action = BrightnessHotkeyAction.MonitorOff, Parameter = parameter, Enabled = true, BindingID = 0
            });
            section.SaveAndApply();
            RebuildShell(BrightnessSettingsPage.Hotkeys);
        };
        stack.Children.Add(add);
    }

    private IEnumerable<string> ExistingMonitorOffParameters() =>
        _settings.Hotkeys
            .Where(static b => b is { RemovedByUser: false, Action: BrightnessHotkeyAction.MonitorOff })
            .Select(static b => b.Parameter)
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<(string Value, string Label)> BuildMonitorTargetOptions()
    {
        List<(string Value, string Label)> targets = [];
        IReadOnlyList<MonitorInfo> live = [.. (_monitorService?.Monitors ?? []).Where(static m => !m.IsMaster)];

        foreach (int displayNumber in live
                     .Where(static m => m.DisplayNumber > 0)
                     .Select(static m => m.DisplayNumber)
                     .Distinct()
                     .OrderBy(static n => n))
        {
            targets.Add((
                HotkeyTarget.ForDisplayNumber(displayNumber),
                string.Format(CultureInfo.CurrentCulture, L(nameof(AppStrings.Settings_Hotkeys_DisplayNumber_Format)),
                    displayNumber)));
        }

        foreach (KnownDisplayEntry known in AppServices.KnownDisplays?.Entries ?? [])
        {
            if (string.IsNullOrWhiteSpace(known.EDIDKey)) continue;
            string baseLabel = !string.IsNullOrWhiteSpace(known.OriginalName)
                ? known.OriginalName
                : L(nameof(AppStrings.Settings_Hotkeys_DisplayFallbackName));
            string serial = string.IsNullOrWhiteSpace(known.EDIDSerial) ? "" : $": {known.EDIDSerial}";
            MonitorInfo? active = live.FirstOrDefault(m => m.EDIDKey == known.EDIDKey);
            string activeSuffix = active is { DisplayNumber: > 0 }
                ? " " + string.Format(CultureInfo.CurrentCulture,
                    L(nameof(AppStrings.Settings_Hotkeys_CurrentlyDisplayNumber_Format)), active.DisplayNumber)
                : "";
            targets.Add((HotkeyTarget.ForEDID(known.EDIDKey), $"{baseLabel}{serial}{activeSuffix}"));
        }

        return [.. targets.DistinctBy(static t => t.Value)];
    }

    private string MonitorTargetLabel(string parameter)
    {
        foreach ((string value, string label) in BuildMonitorTargetOptions())
        {
            if (string.Equals(value, parameter, StringComparison.Ordinal))
                return label;
        }

        if (HotkeyTarget.TryParseDisplayNumber(parameter, out int number))
        {
            return string.Format(CultureInfo.CurrentCulture,
                L(nameof(AppStrings.Settings_Hotkeys_DisplayNumber_Format)),
                number);
        }

        return HotkeyTarget.TryParseEDID(parameter, out string EDIDKey) ? EDIDKey : parameter;
    }
}
