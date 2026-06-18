#pragma warning disable CA1822

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using NetworkTrayAppDotNET.Models;

namespace NetworkTrayAppDotNET.UI;

public sealed partial class NetworkSettingsWindow
{
    private StackPanel BuildHotkeysPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc("Settings_Hotkeys_SectionHeader"), p);
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            Loc("Settings_Hotkeys_SectionDescription"), p, new Thickness(0, 0, 0, 16)));

        TextBox searchBox = TrayAppDotNETSettingsUI.TextBox(p, 240);
        StackPanel searchRow = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
        };
        TextBlock searchLabel = TrayAppDotNETSettingsUI.TitleText(Loc("Settings_Hotkeys_SearchLabel"), p);
        searchLabel.VerticalAlignment = VerticalAlignment.Center;
        searchLabel.Margin = new Thickness(0, 0, 8, 0);
        searchRow.Children.Add(searchLabel);
        searchRow.Children.Add(searchBox);
        stack.Children.Add(searchRow);

        List<(Control Control, string SearchText)> rows = [];
        AddHotkeyRow(stack, rows, HotkeyAction.OpenFlyout,
            L("Settings_Hotkeys_OpenFlyout_Title", "Open flyout"),
            L("Settings_Hotkeys_OpenFlyout_Description", "Show the network flyout above the tray icon."),
            p);
        AddHotkeyRow(stack, rows, HotkeyAction.OpenSettings,
            Loc("Settings_Hotkeys_OpenSettings_Title"),
            Loc("Settings_Hotkeys_OpenSettings_Description"),
            p);

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

    private void AddHotkeyRow(
        StackPanel stack,
        List<(Control Control, string SearchText)> rows,
        HotkeyAction action,
        string title,
        string description,
        SettingsPalette p)
    {
        StackPanel entries = new() { Spacing = 0 };
        uint selectedModifiers = 0;
        uint selectedVk = 0;

        SettingsComboBox modifiers = TrayAppDotNETSettingsUI.ComboBox(p, 170);
        modifiers.Padding = new Thickness(8, 0, 2, 0);
        foreach (TrayAppDotNETHotkeyModifierOption option in HotkeyModifierOptions)
            modifiers.Items.Add(new SettingsComboBoxItem(option.Modifiers, option.Label, p));

        TextBox keyBox = TrayAppDotNETSettingsUI.TextBox(p, 60);
        keyBox.IsReadOnly = true;
        keyBox.Cursor = new Cursor(StandardCursorType.Ibeam);

        SettingsButton addButton = Button(Loc("Settings_Hotkeys_Add_Button"), p);
        addButton.MinWidth = 70;
        addButton.IsEnabled = false;

        void UpdateAddButtonState()
        {
            if (selectedModifiers == 0 || selectedVk == 0)
            {
                addButton.Text = Loc("Settings_Hotkeys_Add_Button");
                addButton.IsEnabled = false;
                return;
            }

            bool exists = _settings.Hotkeys.Any(b =>
                !b.RemovedByUser
                && b.Matches(action, string.Empty)
                && b.Modifiers == selectedModifiers
                && b.VirtualKey == selectedVk);
            addButton.Text = exists
                ? Loc("Settings_Hotkeys_Exists_Button")
                : Loc("Settings_Hotkeys_Add_Button");
            addButton.IsEnabled = !exists;
        }

        void Refresh()
        {
            HotkeyApplyResult? applyResult = null;
            try { applyResult = AppServices.HotkeyService?.Apply(_settings.Hotkeys); }
            catch (Exception ex) { TADNLog.Log($"NetworkAvaloniaApp.Hotkeys.Apply: {ex.Message}"); }

            entries.Children.Clear();
            foreach (HotkeyBinding binding in _settings.Hotkeys
                         .Where(h => !h.RemovedByUser && h.Matches(action, string.Empty)).OrderBy(h => h.BindingID))
                entries.Children.Add(BuildHotkeyEntryCard(action, binding, applyResult, Refresh, p));
            entries.IsVisible = entries.Children.Count > 0;
            UpdateAddButtonState();
        }

        modifiers.SelectionChanged += (_, _) =>
        {
            selectedModifiers = modifiers.SelectedItem is { Tag: uint mods } ? mods : 0;
            UpdateAddButtonState();
        };
        keyBox.KeyDown += (_, e) =>
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape)
            {
                e.Handled = true;
                return;
            }

            uint vk = TrayAppDotNETHotkeyKeys.VirtualKeyFromKey(e.Key);
            if (vk == 0 || vk == 0x7B)
            {
                e.Handled = true;
                return;
            }

            selectedVk = vk;
            keyBox.Text = TrayAppDotNETHotkeyKeys.KeyName(vk);
            UpdateAddButtonState();
            e.Handled = true;
        };
        addButton.Click += (_, _) =>
        {
            if (!addButton.IsEnabled || selectedModifiers == 0 || selectedVk == 0) return;
            int id = _settings.Hotkeys.Where(h => h.Matches(action, string.Empty)).Select(h => h.BindingID)
                .DefaultIfEmpty(0).Max() + 1;
            _settings.Hotkeys.Add(new HotkeyBinding
            {
                Action = action,
                Parameter = string.Empty,
                Modifiers = selectedModifiers,
                VirtualKey = selectedVk,
                Enabled = true,
                BindingID = id,
            });
            selectedModifiers = 0;
            selectedVk = 0;
            modifiers.SelectedIndex = -1;
            keyBox.Text = string.Empty;
            Save();
            Refresh();
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 240 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        StackPanel text = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(title, p));
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
        Grid.SetColumnSpan(entries, 2);
        grid.Children.Add(entries);

        Border card = RawCard(grid, p);
        rows.Add((card, title + "\n" + description));
        stack.Children.Add(card);
        Refresh();
    }

    private Border BuildHotkeyEntryCard(
        HotkeyAction action,
        HotkeyBinding binding,
        HotkeyApplyResult? applyResult,
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
            TrayAppDotNETToolTip.SetTip(status, Loc("Settings_Hotkeys_Status_HotkeyServiceUnavailable"));
        }
        else if (applyResult?.Failed.TryGetValue(binding, out string? error) == true)
        {
            status.Text = GlyphCatalog.WARNING;
            TrayAppDotNETToolTip.SetTip(status, error);
        }
        else if (binding.IsBound) TrayAppDotNETToolTip.SetTip(status, Loc("Settings_Hotkeys_Status_Registered"));

        SettingsButton delete = Button("x", p);
        delete.Width = 32;
        delete.Height = 29;
        delete.Padding = new Thickness(0);
        delete.Label.FontSize = 20;
        TrayAppDotNETToolTip.SetTip(delete, Loc("Settings_Hotkeys_DeleteHotkey_ToolTip"));
        delete.Click += (_, _) =>
        {
            if (AppSettings.IsDefaultHotkeyIdentity(action, string.Empty, binding.BindingID))
            {
                binding.RemovedByUser = true;
                binding.Enabled = false;
            }
            else
                _settings.Hotkeys.RemoveAll(b => b.Matches(action, string.Empty, binding.BindingID));

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

    private static string FormatHotkey(HotkeyBinding binding)
    {
        string modifiers = TrayAppDotNETHotkeyKeys.ModifierText(binding.Modifiers);
        string key = TrayAppDotNETHotkeyKeys.KeyName(binding.VirtualKey);
        return string.IsNullOrEmpty(modifiers) ? key : modifiers + " + " + key;
    }

    private static IReadOnlyList<TrayAppDotNETHotkeyModifierOption> HotkeyModifierOptions =>
        TrayAppDotNETHotkeyModifierOptions.Create(Loc);
}
