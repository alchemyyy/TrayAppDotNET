using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Hotkeys;
using BrightnessHotkeyBinding = BrightnessTrayAppDotNET.Models.HotkeyBinding;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildHotkeysPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_Hotkeys_SectionHeader", "Hotkeys"), p);
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L("Settings_Hotkeys_SectionDescription", "Assign global hotkeys to BrightnessTrayAppDotNET actions."),
            p,
            new Thickness(0, 0, 0, 16)));

        TextBox searchBox = TrayAppDotNETSettingsUI.TextBox(p, 260);
        StackPanel searchRow = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
        };
        TextBlock searchLabel = TrayAppDotNETSettingsUI.TitleText(L("Settings_Hotkeys_SearchLabel", "Search"), p);
        searchLabel.VerticalAlignment = VerticalAlignment.Center;
        searchLabel.Margin = new Thickness(0, 0, 8, 0);
        searchRow.Children.Add(searchLabel);
        searchRow.Children.Add(searchBox);
        stack.Children.Add(searchRow);

        List<(Control Control, string SearchText)> rows = [];
        foreach ((BrightnessHotkeyAction action, string parameter, string title, string description) in HotkeyRows())
            AddHotkeyRow(stack, rows, action, parameter, title, description, removableMonitorTarget: false, p);

        foreach (string parameter in ExistingMonitorOffParameters())
        {
            AddHotkeyRow(
                stack,
                rows,
                BrightnessHotkeyAction.MonitorOff,
                parameter,
                L("Settings_Hotkeys_PowerOffSpecificMonitor_Title", "Power off specific monitor"),
                MonitorTargetLabel(parameter),
                removableMonitorTarget: true,
                p);
        }

        AddMonitorOffBindingButton(stack, p);

        searchBox.TextChanged += (_, _) =>
        {
            string query = (searchBox.Text ?? string.Empty).Trim();
            foreach ((Control row, string searchText) in rows)
            {
                row.IsVisible = query.Length == 0
                                || searchText.Contains(query, StringComparison.OrdinalIgnoreCase);
            }
        };

        return stack;
    }

    private IEnumerable<(BrightnessHotkeyAction Action, string Parameter, string Title, string Description)>
        HotkeyRows()
    {
        yield return (BrightnessHotkeyAction.OpenFlyout, "", L("Settings_Hotkeys_OpenFlyout_Title", "Open flyout"),
            L("Settings_Hotkeys_OpenFlyout_Description", "Show or hide the brightness flyout."));
        yield return (BrightnessHotkeyAction.OpenSettings, "",
            L("Settings_Hotkeys_OpenSettings_Title", "Open settings"),
            L("Settings_Hotkeys_OpenSettings_Description", "Open this settings window."));
        yield return (BrightnessHotkeyAction.FullBright, "", L("Settings_Hotkeys_FullBright_Title", "Full bright"),
            L("Settings_Hotkeys_FullBright_Description", "Raise participating monitors to full brightness."));
        yield return (BrightnessHotkeyAction.FullDim, "", L("Settings_Hotkeys_FullDim_Title", "Full dim"),
            L("Settings_Hotkeys_FullDim_Description", "Lower participating monitors to minimum brightness."));
        yield return (BrightnessHotkeyAction.IncrementMasterBrightness, "",
            L("Settings_Hotkeys_IncrementMasterBrightness_Title", "Increase master brightness"),
            L("Settings_Hotkeys_IncrementMasterBrightness_Description", "Increase the master brightness value."));
        yield return (BrightnessHotkeyAction.DecrementMasterBrightness, "",
            L("Settings_Hotkeys_DecrementMasterBrightness_Title", "Decrease master brightness"),
            L("Settings_Hotkeys_DecrementMasterBrightness_Description", "Decrease the master brightness value."));
        yield return (BrightnessHotkeyAction.NormalizeBrightnesses, "",
            L("Settings_Hotkeys_NormalizeBrightnesses_Title", "Normalize brightnesses"),
            L("Settings_Hotkeys_NormalizeBrightnesses_Description",
                "Sync individual monitors to the current master brightness."));
        yield return (BrightnessHotkeyAction.ToggleNightLight, "",
            L("Settings_Hotkeys_ToggleNightLight_Title", "Toggle night light"),
            L("Settings_Hotkeys_ToggleNightLight_Description", "Toggle Windows Night Light."));
        yield return (BrightnessHotkeyAction.IncrementNightLight, "",
            L("Settings_Hotkeys_IncrementNightLight_Title", "Increase night light"),
            L("Settings_Hotkeys_IncrementNightLight_Description", "Increase Night Light strength."));
        yield return (BrightnessHotkeyAction.DecrementNightLight, "",
            L("Settings_Hotkeys_DecrementNightLight_Title", "Decrease night light"),
            L("Settings_Hotkeys_DecrementNightLight_Description", "Decrease Night Light strength."));
        yield return (BrightnessHotkeyAction.PowerOffAllMonitors, "",
            L("Settings_Hotkeys_PowerOffAllMonitors_Title", "Power off all monitors"),
            L("Settings_Hotkeys_PowerOffAllMonitors_Description",
                "Run the configured power-off command for all monitors."));

        if (_profileManager == null) yield break;

        for (int i = 0; i < _profileManager.Profiles.Profiles.Count; i++)
        {
            string name = _profileManager.GetName(i) is { Length: > 0 } profileName
                ? profileName
                : string.Format(CultureInfo.CurrentCulture,
                    L("Settings_Hotkeys_DefaultProfileName_Format", "Profile {0}"), i + 1);
            yield return (
                BrightnessHotkeyAction.ProfileSelect,
                i.ToString(CultureInfo.InvariantCulture),
                string.Format(CultureInfo.CurrentCulture,
                    L("Settings_Hotkeys_SelectProfile_Title_Format", "Select {0}"), name),
                L("Settings_Hotkeys_SelectProfile_Description", "Select and apply the profile."));
        }
    }

    private void AddMonitorOffBindingButton(StackPanel stack, SettingsPalette p)
    {
        IReadOnlyList<(string Value, string Label)> targets = BuildMonitorTargetOptions();
        SettingsButton add = Button(L("Settings_Hotkeys_AddMonitorOffBinding_Button", "Add monitor-off binding"), p);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 8, 0, 0);
        add.IsEnabled = targets.Count > 0
                        && targets.Any(t => !ExistingMonitorOffParameters().Contains(t.Value, StringComparer.Ordinal));
        add.Click += (_, _) =>
        {
            string parameter = targets
                                   .Select(static t => t.Value)
                                   .FirstOrDefault(v =>
                                       !ExistingMonitorOffParameters().Contains(v, StringComparer.Ordinal))
                               ?? string.Empty;
            if (string.IsNullOrWhiteSpace(parameter)) return;

            _settings.Hotkeys.Add(new BrightnessHotkeyBinding
            {
                Action = BrightnessHotkeyAction.MonitorOff, Parameter = parameter, Enabled = true, BindingID = 0,
            });
            Save();
            RebuildShell(BrightnessSettingsPage.Hotkeys);
        };
        stack.Children.Add(add);
    }

    private void AddHotkeyRow(
        StackPanel stack,
        List<(Control Control, string SearchText)> rows,
        BrightnessHotkeyAction action,
        string parameter,
        string title,
        string description,
        bool removableMonitorTarget,
        SettingsPalette p)
    {
        StackPanel entries = new() { Spacing = 0 };
        uint selectedModifiers = 0;
        uint selectedVirtualKey = 0;
        string currentParameter = parameter;
        SettingsComboBox? targetCombo = null;

        SettingsComboBox modifiers = TrayAppDotNETSettingsUI.ComboBox(p, 170);
        modifiers.Padding = new Thickness(8, 0, 2, 0);
        foreach (TrayAppDotNETHotkeyModifierOption option in HotkeyModifierOptions)
            modifiers.Items.Add(new SettingsComboBoxItem(option.Modifiers, option.Label, p));

        TextBox keyBox = TrayAppDotNETSettingsUI.TextBox(p, 60);
        keyBox.IsReadOnly = true;
        keyBox.Cursor = new Cursor(StandardCursorType.Ibeam);

        SettingsButton addButton = Button(L("Settings_Hotkeys_Add_Button", "Add"), p);
        addButton.MinWidth = 70;
        addButton.IsEnabled = false;

        SettingsButton? removeTarget = null;
        if (removableMonitorTarget)
        {
            removeTarget = Button(L("Settings_Hotkeys_Remove_Button", "Remove"), p);
            removeTarget.Click += (_, _) =>
            {
                _settings.Hotkeys.RemoveAll(b => b.Matches(action, currentParameter));
                Save();
                RebuildShell(BrightnessSettingsPage.Hotkeys);
            };
        }

        if (removableMonitorTarget)
        {
            targetCombo = TrayAppDotNETSettingsUI.ComboBox(p, 240);
            foreach ((string value, string label) in BuildMonitorTargetOptions())
                targetCombo.Items.Add(new SettingsComboBoxItem(value, label, p));
            if (!string.IsNullOrWhiteSpace(currentParameter)
                && !BuildMonitorTargetOptions()
                    .Any(t => string.Equals(t.Value, currentParameter, StringComparison.Ordinal)))
                targetCombo.Items.Add(new SettingsComboBoxItem(currentParameter, MonitorTargetLabel(currentParameter),
                    p));
            TrayAppDotNETSettingsUI.SelectComboByTag(targetCombo, currentParameter);
        }

        void UpdateAddButtonState()
        {
            if (selectedModifiers == 0 || selectedVirtualKey == 0)
            {
                addButton.Text = L("Settings_Hotkeys_Add_Button", "Add");
                addButton.IsEnabled = false;
                return;
            }

            bool exists = _settings.Hotkeys.Any(b =>
                !b.RemovedByUser
                && b.Matches(action, currentParameter)
                && b.Modifiers == selectedModifiers
                && b.VirtualKey == selectedVirtualKey);
            addButton.Text = exists
                ? L("Settings_Hotkeys_Exists_Button", "Exists")
                : L("Settings_Hotkeys_Add_Button", "Add");
            addButton.IsEnabled = !exists;
        }

        modifiers.SelectionChanged += (_, _) =>
        {
            selectedModifiers = modifiers.SelectedItem?.Tag is uint mods ? mods : 0;
            UpdateAddButtonState();
        };
        if (targetCombo != null)
        {
            targetCombo.SelectionChanged += (_, _) =>
            {
                string newParameter = TrayAppDotNETSettingsUI.SelectedTag(targetCombo) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(newParameter)
                    || string.Equals(newParameter, currentParameter, StringComparison.Ordinal))
                    return;

                bool duplicateTarget = ExistingMonitorOffParameters()
                    .Any(existing => !string.Equals(existing, currentParameter, StringComparison.Ordinal)
                                     && string.Equals(existing, newParameter, StringComparison.Ordinal));
                if (duplicateTarget)
                {
                    TrayAppDotNETSettingsUI.SelectComboByTag(targetCombo, currentParameter);
                    return;
                }

                foreach (BrightnessHotkeyBinding binding in _settings.Hotkeys
                             .Where(h => h.Matches(action, currentParameter)).ToArray())
                    binding.Parameter = newParameter;
                currentParameter = newParameter;
                Save();
                Refresh();
            };
        }

        keyBox.KeyDown += (_, e) =>
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape)
            {
                e.Handled = true;
                return;
            }

            uint virtualKey = TrayAppDotNETHotkeyKeys.VirtualKeyFromKey(e.Key);
            if (virtualKey is 0 or 0x7B)
            {
                e.Handled = true;
                return;
            }

            selectedVirtualKey = virtualKey;
            keyBox.Text = TrayAppDotNETHotkeyKeys.KeyName(virtualKey);
            UpdateAddButtonState();
            e.Handled = true;
        };
        addButton.Click += (_, _) =>
        {
            if (!addButton.IsEnabled || selectedModifiers == 0 || selectedVirtualKey == 0) return;
            int bindingID = _settings.Hotkeys.Where(h => h.Matches(action, currentParameter)).Select(h => h.BindingID)
                .DefaultIfEmpty(0).Max() + 1;
            _settings.Hotkeys.Add(new BrightnessHotkeyBinding
            {
                Action = action,
                Parameter = currentParameter,
                Modifiers = selectedModifiers,
                VirtualKey = selectedVirtualKey,
                Enabled = true,
                BindingID = bindingID,
            });
            selectedModifiers = 0;
            selectedVirtualKey = 0;
            modifiers.SelectedIndex = -1;
            keyBox.Text = string.Empty;
            Save();
            Refresh();
        };

        Control content;
        if (removableMonitorTarget && targetCombo != null && removeTarget != null)
        {
            StackPanel panel = new();
            TextBlock rowTitle = TrayAppDotNETSettingsUI.TitleText(title, p);
            rowTitle.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(rowTitle);

            Grid monitorGrid = new();
            monitorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            monitorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            monitorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            monitorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            monitorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            monitorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            monitorGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            monitorGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            targetCombo.Padding = new Thickness(8, 0, 2, 0);
            targetCombo.VerticalAlignment = VerticalAlignment.Center;
            modifiers.Margin = new Thickness(0, 0, 8, 0);
            keyBox.Margin = new Thickness(0, 0, 8, 0);
            addButton.Margin = new Thickness(0, 0, 8, 0);

            monitorGrid.Children.Add(targetCombo);
            Grid.SetColumn(modifiers, 2);
            Grid.SetColumn(keyBox, 3);
            Grid.SetColumn(addButton, 4);
            Grid.SetColumn(removeTarget, 5);
            monitorGrid.Children.Add(modifiers);
            monitorGrid.Children.Add(keyBox);
            monitorGrid.Children.Add(addButton);
            monitorGrid.Children.Add(removeTarget);

            entries.Margin = new Thickness(0, 8, 8, 0);
            Grid.SetRow(entries, 1);
            Grid.SetColumn(entries, 2);
            Grid.SetColumnSpan(entries, 2);
            monitorGrid.Children.Add(entries);

            panel.Children.Add(monitorGrid);
            content = panel;
        }
        else
        {
            Grid grid = new();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 240 });
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            StackPanel text = new()
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
            };
            text.Children.Add(TrayAppDotNETSettingsUI.TitleText(title, p));
            if (!string.IsNullOrWhiteSpace(description))
                text.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(description, p));
            grid.Children.Add(text);

            modifiers.Margin = new Thickness(0, 0, 8, 0);
            keyBox.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(modifiers, 1);
            Grid.SetColumn(keyBox, 2);
            Grid.SetColumn(addButton, 3);
            grid.Children.Add(modifiers);
            grid.Children.Add(keyBox);
            grid.Children.Add(addButton);

            entries.Margin = new Thickness(0, 8, 8, 0);
            Grid.SetRow(entries, 1);
            Grid.SetColumn(entries, 1);
            Grid.SetColumnSpan(entries, 3);
            grid.Children.Add(entries);
            content = grid;
        }

        Border card = RawCard(content, p);
        rows.Add((card, title + "\n" + description));
        stack.Children.Add(card);
        Refresh();
        return;

        void Refresh()
        {
            HotkeyApplyResult<BrightnessHotkeyAction, BrightnessHotkeyBinding>? applyResult = null;
            try { applyResult = AppServices.HotkeyService?.Apply(_settings.Hotkeys); }
            catch (Exception ex) { WPFLog.Log($"BrightnessSettingsWindow.Hotkeys.Apply: {ex.Message}"); }

            entries.Children.Clear();
            foreach (BrightnessHotkeyBinding binding in _settings.Hotkeys
                         .Where(h => !h.RemovedByUser && h.Matches(action, currentParameter) && h.IsBound)
                         .OrderBy(h => h.BindingID))
                entries.Children.Add(BuildHotkeyEntryCard(action, currentParameter, binding, applyResult, Refresh, p));
            entries.IsVisible = entries.Children.Count > 0;
            UpdateAddButtonState();
        }
    }

    private Border BuildHotkeyEntryCard(
        BrightnessHotkeyAction action,
        string parameter,
        BrightnessHotkeyBinding binding,
        HotkeyApplyResult<BrightnessHotkeyAction, BrightnessHotkeyBinding>? applyResult,
        Action refresh,
        SettingsPalette p)
    {
        TextBlock display = TrayAppDotNETSettingsUI.Text(FormatHotkey(binding), p);
        display.VerticalAlignment = VerticalAlignment.Center;
        display.Margin = new Thickness(12, 6, 0, 6);

        TextBlock status = TrayAppDotNETSettingsUI.Text(string.Empty, p);
        status.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(0, 0, 8, 0);

        if (AppServices.HotkeyService == null)
        {
            status.Text = GlyphCatalog.WARNING;
            TrayAppDotNETToolTip.SetTip(status,
                L("Settings_Hotkeys_Status_HotkeyServiceUnavailable", "Hotkey service unavailable."));
        }
        else if (applyResult?.Failed.TryGetValue(binding, out string? error) == true)
        {
            status.Text = GlyphCatalog.WARNING;
            TrayAppDotNETToolTip.SetTip(status, error);
        }
        else if (binding.IsBound)
            TrayAppDotNETToolTip.SetTip(status, L("Settings_Hotkeys_Status_Registered", "Registered."));

        SettingsButton delete = Button("x", p);
        delete.Width = 32;
        delete.Height = 29;
        delete.Padding = new Thickness(0);
        delete.Label.FontSize = 20;
        TrayAppDotNETToolTip.SetTip(delete, L("Settings_Hotkeys_DeleteHotkey_ToolTip", "Delete hotkey"));
        TrayAppDotNETToolTip.SuppressWhileEngaged(delete);
        delete.Click += (_, _) =>
        {
            if (AppSettings.IsDefaultHotkeyIdentity(action, parameter, binding.BindingID))
            {
                foreach (BrightnessHotkeyBinding existing in _settings.Hotkeys)
                {
                    if (!existing.Matches(action, parameter, binding.BindingID)) continue;
                    existing.RemovedByUser = true;
                    existing.Enabled = false;
                }
            }
            else
                _settings.Hotkeys.RemoveAll(b => b.Matches(action, parameter, binding.BindingID));

            Save();
            refresh();
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(display);
        Grid.SetColumn(status, 1);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(status);
        grid.Children.Add(delete);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.ControlBackground),
            CornerRadius = RadiusMedium,
            Margin = new Thickness(0, 0, 0, 4),
            Child = grid,
        };
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
                string.Format(CultureInfo.CurrentCulture, L("Settings_Hotkeys_DisplayNumber_Format", "Display #{0}"),
                    displayNumber)));
        }

        foreach (KnownDisplayEntry known in _settings.KnownDisplays)
        {
            if (string.IsNullOrWhiteSpace(known.EDIDKey)) continue;
            string baseLabel = !string.IsNullOrWhiteSpace(known.OriginalName)
                ? known.OriginalName
                : L("Settings_Hotkeys_DisplayFallbackName", "Display");
            string serial = string.IsNullOrWhiteSpace(known.EDIDSerial) ? "" : $": {known.EDIDSerial}";
            MonitorInfo? active = live.FirstOrDefault(m => m.EDIDKey == known.EDIDKey);
            string activeSuffix = active is { DisplayNumber: > 0 }
                ? " " + string.Format(CultureInfo.CurrentCulture,
                    L("Settings_Hotkeys_CurrentlyDisplayNumber_Format", "currently #{0}"), active.DisplayNumber)
                : "";
            targets.Add((HotkeyTarget.ForEDID(known.EDIDKey), $"{baseLabel}{serial}{activeSuffix}"));
        }

        return [.. targets.DistinctBy(static t => t.Value)];
    }

    private string MonitorTargetLabel(string parameter)
    {
        foreach ((string value, string label) in BuildMonitorTargetOptions())
            if (string.Equals(value, parameter, StringComparison.Ordinal))
                return label;

        if (HotkeyTarget.TryParseDisplayNumber(parameter, out int number))
            return string.Format(CultureInfo.CurrentCulture, L("Settings_Hotkeys_DisplayNumber_Format", "Display #{0}"),
                number);
        if (HotkeyTarget.TryParseEDID(parameter, out string EDIDKey))
            return EDIDKey;
        return parameter;
    }

    private static string FormatHotkey(BrightnessHotkeyBinding binding)
    {
        string modifiers = TrayAppDotNETHotkeyKeys.ModifierText(binding.Modifiers);
        string key = TrayAppDotNETHotkeyKeys.KeyName(binding.VirtualKey);
        return string.IsNullOrEmpty(modifiers) ? key : modifiers + " + " + key;
    }

    private static IReadOnlyList<TrayAppDotNETHotkeyModifierOption> HotkeyModifierOptions =>
        TrayAppDotNETHotkeyModifierOptions.Create(L);
}
