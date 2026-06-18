using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildMonitorsPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_Monitors_SectionHeader", "Monitors"), p);

        stack.Children.Add(IntCard(L("Settings_Monitors_BrightnessRate_Title", "Brightness update rate"),
            L("Settings_Monitors_BrightnessRate_Description", "Dwell between brightness writes."),
            _settings.BrightnessUpdateRateMs, TimeConstants.BrightnessUpdateRateMinMs, 10_000,
            v => _settings.BrightnessUpdateRateMs = v, p, Loc("Common_MillisecondsSuffix")));
        stack.Children.Add(IntCard(L("Settings_Monitors_ValidationDwell_Title", "Validation dwell"), "",
            _settings.ValidationDwellMs, 0, 10_000, v => _settings.ValidationDwellMs = v, p,
            Loc("Common_MillisecondsSuffix")));
        stack.Children.Add(IntCard(L("Settings_Monitors_ValidationAttempts_Title", "Validation attempts"), "",
            _settings.ValidationAttempts, 1, 20, v => _settings.ValidationAttempts = v, p));
        stack.Children.Add(IntCard(L("Settings_Monitors_DDCOperationTimeout_Title", "DDC operation timeout"), "",
            _settings.DDCOperationTimeoutMs, 0, 60_000, v => _settings.DDCOperationTimeoutMs = v, p,
            Loc("Common_MillisecondsSuffix")));
        stack.Children.Add(StringComboCard(L("Settings_Monitors_PowerOffMode_Title", "Power-off mode"),
            L("Settings_Monitors_PowerOffMode_Description", "VCP command used by monitor power buttons."),
            PowerOffOptions(), _settings.PowerOffMode, v => _settings.PowerOffMode = v, p));
        stack.Children.Add(StringComboCard(
            L("Settings_Monitors_IdentityStrategy_Title", "Monitor identity"),
            L("Settings_Monitors_IdentityStrategy_Description", "Key used for profile monitor entries."),
            [
                (MonitorIdentityStrategy.DisplayNumber,
                    L("Settings_Monitors_Identity_DisplayNumber", "Display number")),
                (MonitorIdentityStrategy.HardwarePort, L("Settings_Monitors_Identity_HardwarePort", "Hardware port")),
                (MonitorIdentityStrategy.EDIDSerial, L("Settings_Monitors_Identity_EDIDSerial", "EDID serial")),
            ],
            _settings.MonitorIdentityStrategy,
            v => _settings.MonitorIdentityStrategy = v,
            p));
        stack.Children.Add(ComboCard(
            L("Settings_Monitors_DefaultSort_Title", "Default sort"),
            "",
            [
                ("Arrangement", L("Settings_Monitors_Sort_Arrangement", "Arrangement")),
                ("ArrangementRev", L("Settings_Monitors_Sort_ArrangementRev", "Arrangement, reversed")),
                ("DisplayNumber", L("Settings_Monitors_Sort_DisplayNumber", "Display number")),
                ("DisplayNumberRev", L("Settings_Monitors_Sort_DisplayNumberRev", "Display number, reversed")),
            ],
            ComposeDefaultSortTag(_settings.DefaultDisplaySortMode, _settings.DefaultDisplaySortDirection),
            ApplyDefaultSortTag,
            p));

        SettingsButton clear = Button(L("Settings_Monitors_ClearDisplays_Button", "Clear saved displays"), p);
        clear.Click += async (_, _) =>
        {
            bool ok = await ConfirmAsync(
                L("Settings_Monitors_ClearDisplays_ConfirmTitle", "Clear saved displays?"),
                L("Settings_Monitors_ClearDisplays_ConfirmMessage",
                    "This removes saved display names, order, and monitor-specific overrides."),
                L("Settings_Monitors_ClearDisplays_ConfirmButton", "Clear"),
                L("Common_Cancel", "Cancel"));
            if (!ok) return;

            _settings.MonitorOrder.Clear();
            _settings.MonitorOverrides.Clear();
            _settings.KnownDisplays.Clear();
            Save();
            RebuildShell(BrightnessSettingsPage.Monitors);
        };
        stack.Children.Add(Card(
            L("Settings_Monitors_DisplayOrder_Title", "Display order and overrides"),
            L("Settings_Monitors_DisplayOrder_Description",
                "Connected and previously seen displays with saved per-monitor options."),
            clear,
            p));

        foreach (MonitorSettingsRow row in BuildMonitorRows())
            stack.Children.Add(BuildMonitorOverrideCard(row, p));

        return stack;
    }

    private Border BuildMonitorOverrideCard(MonitorSettingsRow row, SettingsPalette p)
    {
        StackPanel content = new();
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText(row.DisplayName, p));
        if (!string.IsNullOrWhiteSpace(row.Detail))
            content.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(row.Detail, p));
        Grid controls = new() { Margin = new Thickness(0, 12, 0, 0) };
        controls.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        controls.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(12)));
        controls.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        controls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        controls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        controls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddTextOverride(controls, 0, 0, L("Settings_Monitors_Name_Label", "Name"), row.Override.Name,
            text => UpdateMonitorOverride(row.EDIDKey, o => o.Name = text.Trim()));
        AddNumberOverride(controls, 0, 2, L("Settings_Monitors_MinBrightnessOverride_Label", "Min brightness"),
            row.Override.MinBrightness, 0, 100,
            value => UpdateMonitorOverride(row.EDIDKey, o => o.MinBrightness = value));
        AddNumberOverride(controls, 1, 0, L("Settings_Monitors_MaxBrightnessOverride_Label", "Max brightness"),
            row.Override.MaxBrightness, 0, 100,
            value => UpdateMonitorOverride(row.EDIDKey, o => o.MaxBrightness = value));
        AddNumberOverride(controls, 1, 2, L("Settings_Monitors_ValidationDwellOverride_Label", "Validation dwell"),
            row.Override.ValidationDwellMs, -1, 10_000,
            value => UpdateMonitorOverride(row.EDIDKey, o => o.ValidationDwellMs = value));
        AddNumberOverride(controls, 2, 0, L("Settings_Monitors_BrightnessDwellOverride_Label", "Brightness dwell"),
            row.Override.BrightnessDwellMs, -1, 10_000,
            value => UpdateMonitorOverride(row.EDIDKey, o => o.BrightnessDwellMs = value));
        AddTextOverride(controls, 2, 2, L("Settings_Monitors_PowerOffVcpOverride_Label", "Power-off VCP"),
            row.Override.PowerOffVcpOverride,
            text => UpdateMonitorOverride(row.EDIDKey, o => o.PowerOffVcpOverride = text.Trim()));
        content.Children.Add(controls);
        return RawCard(content, p);
    }

    private void AddTextOverride(Grid grid, int row, int column, string label, string value, Action<string> set)
    {
        SettingsPalette p = Palette;
        StackPanel cell = new();
        cell.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(label, p, new Thickness(0, 0, 0, 4)));
        TextBox box = TrayAppDotNETSettingsUI.TextBox(p, double.NaN, value);
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.LostFocus += (_, _) =>
        {
            set(box.Text ?? string.Empty);
            Save();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            set(box.Text ?? string.Empty);
            Save();
            e.Handled = true;
        };
        cell.Children.Add(box);
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void AddNumberOverride(Grid grid, int row, int column, string label, int value, int min, int max,
        Action<int> set)
    {
        SettingsPalette p = Palette;
        StackPanel cell = new();
        cell.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(label, p, new Thickness(0, 0, 0, 4)));
        SettingsNumberBox box = TrayAppDotNETSettingsUI.NumberBox(p, value, min, max, 96);
        box.ValueChanged += (_, e) =>
        {
            if (!e.NewValue.HasValue) return;
            set((int)Math.Round(e.NewValue.Value));
            Save();
        };
        cell.Children.Add(box);
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private List<MonitorSettingsRow> BuildMonitorRows()
    {
        Dictionary<string, MonitorInfo> live = (_monitorService?.Monitors ?? [])
            .Where<MonitorInfo>(static m => !m.IsMaster && !string.IsNullOrWhiteSpace(m.EDIDKey))
            .GroupBy(static m => m.EDIDKey, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);
        HashSet<string> keys = new(live.Keys, StringComparer.Ordinal);
        foreach (KnownDisplayEntry known in _settings.KnownDisplays)
            if (!string.IsNullOrWhiteSpace(known.EDIDKey))
                keys.Add(known.EDIDKey);
        foreach (MonitorOverrideEntry ov in _settings.MonitorOverrides)
            if (!string.IsNullOrWhiteSpace(ov.ID))
                keys.Add(ov.ID);
        List<MonitorSettingsRow> rows = [];
        foreach (string EDIDKey in keys.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase))
        {
            live.TryGetValue(EDIDKey, out MonitorInfo? monitor);
            KnownDisplayEntry? known = _settings.KnownDisplays.FirstOrDefault(k => k.EDIDKey == EDIDKey);
            MonitorOverrideEntry ov = FindOrCreateMonitorOverride(EDIDKey);
            string displayName = !string.IsNullOrWhiteSpace(ov.Name)
                ? ov.Name
                : monitor?.Name ?? known?.OriginalName ?? L("Settings_Monitors_DisplayFallback_Name", "Display");
            string detail = monitor is { DisplayNumber: > 0 }
                ? string.Format(CultureInfo.CurrentCulture,
                    L("Settings_Hotkeys_CurrentlyDisplayNumber_Format", "Currently #{0}"), monitor.DisplayNumber)
                : L("Settings_Monitors_DisconnectedDisplay_Label", "Disconnected");
            rows.Add(new MonitorSettingsRow(EDIDKey, displayName, detail, ov));
        }

        return rows;
    }

    private MonitorOverrideEntry FindOrCreateMonitorOverride(string EDIDKey)
    {
        MonitorOverrideEntry? existing =
            _settings.MonitorOverrides.FirstOrDefault<MonitorOverrideEntry>(o => o.ID == EDIDKey);
        if (existing != null) return existing;
        existing = new MonitorOverrideEntry { ID = EDIDKey };
        _settings.MonitorOverrides.Add(existing);
        return existing;
    }

    private void UpdateMonitorOverride(string EDIDKey, Action<MonitorOverrideEntry> update)
    {
        MonitorOverrideEntry entry = FindOrCreateMonitorOverride(EDIDKey);
        update(entry);
        PruneMonitorOverride(entry);
    }

    private void PruneMonitorOverride(MonitorOverrideEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Name)) return;
        if (!string.IsNullOrWhiteSpace(entry.PowerOffVcpOverride)) return;
        if (!string.IsNullOrWhiteSpace(entry.BrightnessVcpOverride)) return;
        if (entry.ValidationDwellMs > 0) return;
        if (entry.BrightnessDwellMs > 0) return;
        if (entry.MinBrightness > 0) return;
        if (entry.MaxBrightness < 100) return;
        if (entry.NormCurvePoints.Count > 0) return;
        _settings.MonitorOverrides.Remove(entry);
    }

    private static IReadOnlyList<(PowerOffMode Value, string Text)> PowerOffOptions() =>
    [
        (PowerOffMode.Sleep, L("Settings_Monitors_PowerOff_Sleep", "Sleep")),
        (PowerOffMode.Soft, L("Settings_Monitors_PowerOff_Soft", "Soft")),
        (PowerOffMode.Hard, L("Settings_Monitors_PowerOff_Hard", "Hard")),
    ];

    private void ApplyDefaultSortTag(string tag)
    {
        if (!TryParseDefaultSortTag(tag, out DisplaySortMode mode, out DisplaySortDirection direction)) return;
        _settings.DefaultDisplaySortMode = mode;
        _settings.DefaultDisplaySortDirection = direction;
    }

    private static string ComposeDefaultSortTag(DisplaySortMode mode, DisplaySortDirection direction) =>
        (mode, direction) switch
        {
            (DisplaySortMode.Arrangement, DisplaySortDirection.Reversed) => "ArrangementRev",
            (DisplaySortMode.DisplayNumber, DisplaySortDirection.Standard) => "DisplayNumber",
            (DisplaySortMode.DisplayNumber, DisplaySortDirection.Reversed) => "DisplayNumberRev",
            _ => "Arrangement",
        };

    private static bool TryParseDefaultSortTag(string? tag, out DisplaySortMode mode,
        out DisplaySortDirection direction)
    {
        switch (tag)
        {
            case "Arrangement":
                mode = DisplaySortMode.Arrangement;
                direction = DisplaySortDirection.Standard;
                return true;
            case "ArrangementRev":
                mode = DisplaySortMode.Arrangement;
                direction = DisplaySortDirection.Reversed;
                return true;
            case "DisplayNumber":
                mode = DisplaySortMode.DisplayNumber;
                direction = DisplaySortDirection.Standard;
                return true;
            case "DisplayNumberRev":
                mode = DisplaySortMode.DisplayNumber;
                direction = DisplaySortDirection.Reversed;
                return true;
            default:
                mode = DisplaySortMode.Arrangement;
                direction = DisplaySortDirection.Standard;
                return false;
        }
    }

    private sealed record MonitorSettingsRow(
        string EDIDKey,
        string DisplayName,
        string Detail,
        MonitorOverrideEntry Override);
}
