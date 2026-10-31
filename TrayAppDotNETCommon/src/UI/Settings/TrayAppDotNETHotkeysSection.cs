using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Hotkeys;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Settings;

/// <summary>One hotkey card: an (action, parameter) pair the user can bind any number of key combinations to.</summary>
public sealed class TrayAppDotNETHotkeyRow<TAction>
    where TAction : struct, Enum
{
    public required TAction Action { get; init; }
    public string Parameter { get; init; } = string.Empty;
    public required string Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> SearchKeywords { get; init; } = [];

    /// <summary>
    /// App controls placed before the modifier box (slot <c>RowLeading</c>) and after the Add button (slot
    /// <c>RowTrailing</c>), such as Brightness's monitor target picker and its remove button.
    /// </summary>
    public Func<TrayAppDotNETHotkeyRowContext, TrayAppDotNETHotkeyRowExtras>? BuildExtras { get; init; }
}

public sealed record TrayAppDotNETHotkeyRowExtras(Control? Leading, Control? Trailing);

/// <summary>What a row's extras can do with their row.</summary>
public sealed class TrayAppDotNETHotkeyRowContext
{
    private readonly Action<string> _changeParameter;

    internal TrayAppDotNETHotkeyRowContext(Func<string> parameter, Action<string> changeParameter)
    {
        _parameter = parameter;
        _changeParameter = changeParameter;
    }

    private readonly Func<string> _parameter;

    /// <summary>The row's current parameter.</summary>
    public string Parameter => _parameter();

    /// <summary>Moves every binding of the row to <paramref name="parameter"/>, saves and applies once.</summary>
    public void ChangeParameter(string parameter) => _changeParameter(parameter);
}

public sealed class TrayAppDotNETHotkeysSectionOptions<TAction, TBinding>
    where TAction : struct, Enum
    where TBinding : HotkeyBindingBase<TAction>, new()
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required CornerRadius ButtonRadius { get; init; }
    public required Func<string, string> L { get; init; }
    public required Action Save { get; init; }
    public required AppSettingsCommon<TBinding, TAction> Settings { get; init; }

    /// <summary>The app's running hotkey service; null while it failed to start.</summary>
    public required Func<GlobalHotkeyService<TAction, TBinding>?> Service { get; init; }

    public required IReadOnlyList<TrayAppDotNETHotkeyRow<TAction>> Rows { get; init; }
}

/// <summary>
/// The Hotkeys page body every app with global hotkeys shares: one card per row with a modifier box, a key capture
/// box, Add, the existing bindings with their registration status, and a feedback line. Building reads the service's
/// last result; every edit saves and applies once, then refreshes all cards, because one edit can change another
/// card's duplicate status.
/// </summary>
public sealed class TrayAppDotNETHotkeysSection<TAction, TBinding>
    where TAction : struct, Enum
    where TBinding : HotkeyBindingBase<TAction>, new()
{
    private readonly TrayAppDotNETHotkeysSectionOptions<TAction, TBinding> _options;
    private readonly List<RowState> _rows = [];

    private static TrayAppDotNETHotkeysSectionResources.HotkeysSectionAxamlProperties Layout =>
        TrayAppDotNETHotkeysSectionResources.Current.AxamlHotkeysSection;

    public TrayAppDotNETHotkeysSection(TrayAppDotNETHotkeysSectionOptions<TAction, TBinding> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    private List<TBinding> Bindings => _options.Settings.Hotkeys;

    /// <summary>Adds one card per row to <paramref name="stack"/>.</summary>
    public void AddTo(StackPanel stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        foreach (TrayAppDotNETHotkeyRow<TAction> row in _options.Rows)
            stack.Children.Add(BuildRow(row));
        RefreshRows();
    }

    /// <summary>Saves, applies the bindings once, and refreshes every card.</summary>
    public void SaveAndApply()
    {
        _options.Save();
        try { _options.Service()?.Apply(Bindings); }
        catch (Exception ex) { TADNLog.Log($"Hotkeys section Apply failed: {ex}"); }

        RefreshRows();
    }

    /// <summary>Text for a registration failure, naming the conflicting row for a duplicate inside the app.</summary>
    public string DescribeFailure(HotkeyRegistrationFailure<TBinding> failure) => failure.Error switch
    {
        HotkeyRegistrationError.NoKey => L(nameof(CommonStrings.Settings_Hotkeys_Error_NoKey)),
        HotkeyRegistrationError.NoModifier => L(nameof(CommonStrings.Settings_Hotkeys_Error_NoModifier)),
        HotkeyRegistrationError.ReservedKey => L(nameof(CommonStrings.Settings_Hotkeys_Error_ReservedKey)),
        HotkeyRegistrationError.InUseByAnotherApp => L(nameof(CommonStrings.Settings_Hotkeys_Error_InUseByAnotherApp)),
        HotkeyRegistrationError.DuplicateInApp => string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(CommonStrings.Settings_Hotkeys_Error_DuplicateInApp_Format)),
            failure.ConflictingBinding is { } owner ? TitleOf(owner.Action, owner.Parameter) : string.Empty),
        _ => string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(CommonStrings.Settings_Hotkeys_Error_Failed_Format)),
            failure.Win32Error)
    };

    /// <summary>The live binding other than (action, parameter) that already uses the combination, if any.</summary>
    internal TBinding? FindChordOwner(uint modifiers, uint virtualKey) =>
        Bindings.FirstOrDefault(binding => binding.HasSameChord(modifiers, virtualKey));

    private Border BuildRow(TrayAppDotNETHotkeyRow<TAction> row)
    {
        SettingsPalette p = _options.Palette;
        RowState state = new(row);
        _rows.Add(state);

        SettingsComboBox modifiers = TrayAppDotNETSettingsUI.ComboBox(p, width: Layout.ModifierWidth)
            .MapTo(ControlMap.HotkeysSection.Hotkey.Modifiers);
        modifiers.Padding = Layout.ModifierPadding;
        modifiers.Margin = Layout.ControlSpacing;
        foreach (TrayAppDotNETHotkeyModifierOption option in TrayAppDotNETHotkeyModifierOptions.Create(_options.L))
            modifiers.Items.Add(new SettingsComboBoxItem(option.Modifiers, option.Label, p));

        TextBox keyBox = TrayAppDotNETSettingsUI.TextBox(p, width: Layout.KeyWidth)
            .MapTo(ControlMap.HotkeysSection.Hotkey.Key);
        keyBox.IsReadOnly = true;
        keyBox.Cursor = TrayAppDotNETCursors.IBeam;
        keyBox.Margin = Layout.ControlSpacing;

        SettingsButton addButton = Button(L(nameof(CommonStrings.Settings_Hotkeys_Add_Button)))
            .MapTo(ControlMap.HotkeysSection.Hotkey.Add);
        addButton.MinWidth = Layout.AddMinWidth;
        addButton.IsEnabled = false;

        TextBlock feedback = TrayAppDotNETSettingsUI.DescriptionText(string.Empty, p);
        feedback.IsVisible = false;
        feedback.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        feedback.Margin = Layout.FeedbackMargin;

        StackPanel entries = new() { Margin = Layout.EntriesMargin };

        state.Modifiers = modifiers;
        state.KeyBox = keyBox;
        state.Add = addButton;
        state.Feedback = feedback;
        state.Entries = entries;

        modifiers.SelectionChanged += (_, _) =>
        {
            state.SelectedModifiers = modifiers.SelectedItem is { Tag: uint mods } ? mods : 0;
            state.RejectedKeyMessage = null;
            UpdateAddState(state);
        };
        TrayAppDotNETHotkeyKeys.AttachKeyCapture(
            keyBox,
            virtualKey =>
            {
                state.SelectedVirtualKey = virtualKey;
                state.RejectedKeyMessage = null;
                UpdateAddState(state);
            },
            rejected =>
            {
                state.RejectedKeyMessage = rejected;
                UpdateAddState(state);
            });
        addButton.Click += (_, _) => AddBinding(state);

        TrayAppDotNETHotkeyRowExtras? extras = row.BuildExtras?.Invoke(new TrayAppDotNETHotkeyRowContext(
            () => state.Parameter,
            parameter => ChangeParameter(state, parameter)));

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = Layout.TitleMinWidth });
        for (int column = 1; column <= 5; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        StackPanel text = new() { VerticalAlignment = VerticalAlignment.Center, Margin = Layout.TitleMargin };
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(row.Title, p));
        if (!string.IsNullOrWhiteSpace(row.Description))
            text.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(row.Description, p));
        grid.Children.Add(text);

        AddToGrid(grid, extras?.Leading, column: 1);
        AddToGrid(grid, modifiers, column: 2);
        AddToGrid(grid, keyBox, column: 3);
        AddToGrid(grid, addButton, column: 4);
        if (extras?.Trailing is { } trailing)
        {
            trailing.Margin = Layout.TrailingMargin;
            AddToGrid(grid, trailing, column: 5);
        }

        Grid.SetRow(entries, value: 1);
        Grid.SetColumn(entries, value: 2);
        Grid.SetColumnSpan(entries, value: 4);
        grid.Children.Add(entries);

        Grid.SetRow(feedback, value: 2);
        Grid.SetColumnSpan(feedback, value: 6);
        grid.Children.Add(feedback);

        Border card = TrayAppDotNETSettingsCards.RawCard(grid, p, _options.CardRadius)
            .MapTo(ControlMap.HotkeysSection.Hotkey.ID);
        List<string> keywords = [row.Description, .. row.SearchKeywords];
        return SettingsSearchMetadata.MarkCard(card, row.Title, keywords);
    }

    private static void AddToGrid(Grid grid, Control? control, int column)
    {
        if (control == null) return;

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private void AddBinding(RowState state)
    {
        if (!state.Add!.IsEnabled || state.SelectedModifiers == 0 || state.SelectedVirtualKey == 0) return;

        int bindingID = Bindings
            .Where(binding => binding.Matches(state.Row.Action, state.Parameter))
            .Select(binding => binding.BindingID)
            .DefaultIfEmpty(0)
            .Max() + 1;
        Bindings.Add(new TBinding
        {
            Action = state.Row.Action,
            Parameter = state.Parameter,
            Modifiers = state.SelectedModifiers,
            VirtualKey = state.SelectedVirtualKey,
            Enabled = true,
            BindingID = bindingID
        });
        state.SelectedModifiers = 0;
        state.SelectedVirtualKey = 0;
        state.Modifiers!.SelectedIndex = -1;
        state.KeyBox!.Text = string.Empty;
        SaveAndApply();
    }

    private void DeleteBinding(RowState state, TBinding binding)
    {
        TAction action = state.Row.Action;
        if (_options.Settings.IsDefaultHotkeyIdentity(action, state.Parameter, binding.BindingID))
        {
            foreach (TBinding existing in Bindings)
            {
                if (!existing.Matches(action, state.Parameter, binding.BindingID)) continue;

                existing.RemovedByUser = true;
                existing.Enabled = false;
            }
        }
        else
            Bindings.RemoveAll(existing => existing.Matches(action, state.Parameter, binding.BindingID));

        SaveAndApply();
    }

    private void ChangeParameter(RowState state, string parameter)
    {
        if (string.Equals(parameter, state.Parameter, StringComparison.Ordinal)) return;

        foreach (TBinding binding in Bindings.Where(b => b.Matches(state.Row.Action, state.Parameter)).ToArray())
            binding.Parameter = parameter;
        state.Parameter = parameter;
        SaveAndApply();
    }

    private void RefreshRows()
    {
        HotkeyApplyResult<TBinding>? result = _options.Service()?.LastResult;
        foreach (RowState state in _rows)
        {
            StackPanel entries = state.Entries!;
            entries.Children.Clear();
            foreach (TBinding binding in Bindings
                         .Where(b => !b.RemovedByUser && b.IsBound && b.Matches(state.Row.Action, state.Parameter))
                         .OrderBy(b => b.BindingID))
                entries.Children.Add(BuildEntry(state, binding, result));
            entries.IsVisible = entries.Children.Count > 0;
            UpdateAddState(state);
        }
    }

    private Border BuildEntry(RowState state, TBinding binding, HotkeyApplyResult<TBinding>? result)
    {
        SettingsPalette p = _options.Palette;
        TextBlock display = TrayAppDotNETSettingsUI.Text(
            TrayAppDotNETHotkeyKeys.BindingText(binding.Modifiers, binding.VirtualKey),
            p);
        display.VerticalAlignment = VerticalAlignment.Center;
        display.Margin = Layout.EntryTextMargin;

        TextBlock status = TrayAppDotNETSettingsUI.Text(string.Empty, p);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = Layout.StatusMargin;

        string? problem = _options.Service() == null
            ? L(nameof(CommonStrings.Settings_Hotkeys_Status_HotkeyServiceUnavailable))
            : result != null && result.Failed.TryGetValue(binding, out HotkeyRegistrationFailure<TBinding>? failure)
                ? DescribeFailure(failure)
                : null;
        if (problem != null)
        {
            GlyphApplicator.ApplyTo(status, GlyphCatalog.WARNING);
            TrayAppDotNETToolTip.SetTip(status, problem);
            TrayAppDotNETToolTip.SetTip(display, problem);
        }
        else
            TrayAppDotNETToolTip.SetTip(display, L(nameof(CommonStrings.Settings_Hotkeys_Status_Registered)));

        SettingsButton delete = Button(GlyphCatalog.CHROME_CLOSE).MapTo(ControlMap.HotkeysSection.Hotkey.DeleteBinding);
        delete.Width = Layout.DeleteWidth;
        delete.Height = Layout.DeleteHeight;
        delete.Padding = new Thickness(0);
        delete.Label.FontSize = TrayAppDotNETSettingsUI.CloseGlyphFontSize;
        TrayAppDotNETToolTip.SetTip(delete, L(nameof(CommonStrings.Settings_Hotkeys_DeleteHotkey_ToolTip)));
        TrayAppDotNETToolTip.SuppressWhileEngaged(delete);
        delete.Click += (_, _) => DeleteBinding(state, binding);

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(display);
        Grid.SetColumn(status, value: 1);
        Grid.SetColumn(delete, value: 2);
        grid.Children.Add(status);
        grid.Children.Add(delete);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.ControlBackground),
            CornerRadius = _options.ButtonRadius,
            Margin = Layout.EntryMargin,
            Child = grid
        };
    }

    private void UpdateAddState(RowState state)
    {
        SettingsButton add = state.Add!;
        string? feedback = state.RejectedKeyMessage;
        bool enabled = false;
        string addText = L(nameof(CommonStrings.Settings_Hotkeys_Add_Button));
        if (state.SelectedModifiers != 0 && state.SelectedVirtualKey != 0)
        {
            TBinding? owner = FindChordOwner(state.SelectedModifiers, state.SelectedVirtualKey);
            if (owner == null)
                enabled = true;
            else if (owner.Matches(state.Row.Action, state.Parameter))
                addText = L(nameof(CommonStrings.Settings_Hotkeys_Exists_Button));
            else
            {
                feedback = string.Format(
                    CultureInfo.CurrentCulture,
                    L(nameof(CommonStrings.Settings_Hotkeys_ChordInUse_Format)),
                    TrayAppDotNETHotkeyKeys.BindingText(state.SelectedModifiers, state.SelectedVirtualKey),
                    TitleOf(owner.Action, owner.Parameter));
            }
        }

        add.Text = addText;
        add.IsEnabled = enabled;
        TextBlock feedbackText = state.Feedback!;
        feedbackText.Text = feedback ?? string.Empty;
        feedbackText.IsVisible = !string.IsNullOrEmpty(feedback);
    }

    private string TitleOf(TAction action, string parameter)
    {
        foreach (RowState row in _rows)
        {
            if (EqualityComparer<TAction>.Default.Equals(row.Row.Action, action)
                && string.Equals(row.Parameter, parameter, StringComparison.Ordinal))
                return row.Row.Title;
        }

        return string.IsNullOrEmpty(parameter) ? action.ToString() : $"{action} ({parameter})";
    }

    private SettingsButton Button(string text) =>
        TrayAppDotNETSettingsCards.Button(text, _options.Palette, _options.ButtonRadius);

    private SettingsButton Button(Glyph glyph) =>
        TrayAppDotNETSettingsCards.Button(glyph, _options.Palette, _options.ButtonRadius);

    private string L(string key) => _options.L(key);

    private sealed class RowState(TrayAppDotNETHotkeyRow<TAction> row)
    {
        public TrayAppDotNETHotkeyRow<TAction> Row { get; } = row;
        public string Parameter { get; set; } = row.Parameter;
        public uint SelectedModifiers { get; set; }
        public uint SelectedVirtualKey { get; set; }
        public string? RejectedKeyMessage { get; set; }
        public SettingsComboBox? Modifiers { get; set; }
        public TextBox? KeyBox { get; set; }
        public SettingsButton? Add { get; set; }
        public TextBlock? Feedback { get; set; }
        public StackPanel? Entries { get; set; }
    }
}
