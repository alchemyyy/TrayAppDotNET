using Avalonia;
using Avalonia.Controls;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Base window for live editing of one Details column's display properties.</summary>
internal abstract class ProcessColumnPropertiesWindow : Window, IDisposable
{
    private const double WindowWidth = 460;
    private const double DefaultWindowHeight = 280;
    private const double ControlWidth = 190;
    private const double ContentPadding = 16;

    private readonly List<IDisposable> _ownedControls = [];
    private readonly TextBox _nicknameTextBox;
    private Action<ProcessColumnSetting>? _apply;
    private int _disposed;

    protected ProcessColumnPropertiesWindow(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        Action<ProcessColumnSetting> apply)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(apply);
        if (!Enum.IsDefined(setting.Column))
            throw new ArgumentOutOfRangeException(nameof(setting), "The column kind is not defined.");

        Setting = ProcessColumnSettings.Clone(setting);
        Palette = palette;
        _apply = apply;

        ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
        Title = definition.Title;
        Width = WindowWidth;
        Height = DefaultWindowHeight;
        MinWidth = WindowWidth;
        MinHeight = DefaultWindowHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);

        ContentStack = TrayAppDotNETSettingsUI.PageStack("Column properties", palette);
        _nicknameTextBox = TrayAppDotNETSettingsUI.TextBox(palette, ControlWidth, Setting.Nickname);
        _nicknameTextBox.PlaceholderText = definition.Title;
        _nicknameTextBox.TextChanged += OnNicknameTextChanged;
        AddCard(
            "Column nickname",
            "Leave blank to use the original column name.",
            _nicknameTextBox);

        SettingsScrollHost scrollHost = TrayAppDotNETSettingsUI.ScrollHost(
            ContentStack,
            palette,
            new Thickness(ContentPadding));
        Own(scrollHost);
        Content = scrollHost;
        Closed += OnClosed;
    }

    protected ProcessColumnSetting Setting { get; }

    protected SettingsPalette Palette { get; }

    protected StackPanel ContentStack { get; }

    /// <summary>Creates the property-window specialization appropriate for a column.</summary>
    public static ProcessColumnPropertiesWindow Create(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        Action<ProcessColumnSetting> apply)
    {
        ArgumentNullException.ThrowIfNull(setting);

        return setting.Column switch
        {
            ProcessTableColumnKind.CPU or ProcessTableColumnKind.CPUUtility =>
                new CPUProcessColumnPropertiesWindow(setting, palette, apply),
            ProcessTableColumnKind.UserName =>
                new UserNameProcessColumnPropertiesWindow(setting, palette, apply),
            _ when ProcessColumnSettings.IsMemoryColumn(setting.Column) =>
                new MemoryProcessColumnPropertiesWindow(setting, palette, apply),
            _ => new DefaultProcessColumnPropertiesWindow(setting, palette, apply)
        };
    }

    protected void AddCard(string title, string description, Control control) =>
        ContentStack.Children.Add(TrayAppDotNETSettingsUI.Card(title, description, control, Palette));

    protected void Own(IDisposable control) => _ownedControls.Add(control);

    protected void Publish()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _apply?.Invoke(ProcessColumnSettings.Clone(Setting));
    }

    private void OnNicknameTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        Setting.Nickname = _nicknameTextBox.Text ?? string.Empty;
        Publish();
    }

    private void OnClosed(object? sender, EventArgs eventArgs) => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Closed -= OnClosed;
        _nicknameTextBox.TextChanged -= OnNicknameTextChanged;
        for (int controlIndex = _ownedControls.Count - 1; controlIndex >= 0; controlIndex--)
            _ownedControls[controlIndex].Dispose();
        _ownedControls.Clear();
        Content = null;
        _apply = null;
    }
}

internal sealed class DefaultProcessColumnPropertiesWindow : ProcessColumnPropertiesWindow
{
    public DefaultProcessColumnPropertiesWindow(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        Action<ProcessColumnSetting> apply)
        : base(setting, palette, apply)
    {
    }
}

internal sealed class CPUProcessColumnPropertiesWindow : ProcessColumnPropertiesWindow
{
    private const double WindowHeight = 410;

    public CPUProcessColumnPropertiesWindow(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        Action<ProcessColumnSetting> apply)
        : base(setting, palette, apply)
    {
        Height = WindowHeight;
        MinHeight = WindowHeight;

        SettingsToggle percentSuffixToggle = TrayAppDotNETSettingsUI.Toggle(
            palette,
            Setting.ShowPercentSuffix,
            (_, isChecked) =>
            {
                Setting.ShowPercentSuffix = isChecked;
                Publish();
            });
        AddCard(
            "Show % suffix",
            "Append a percent sign to CPU usage values.",
            percentSuffixToggle);

        SettingsToggle decimalUsageToggle = TrayAppDotNETSettingsUI.Toggle(
            palette,
            Setting.ShowDecimalUsage,
            (_, isChecked) =>
            {
                Setting.ShowDecimalUsage = isChecked;
                Publish();
            });
        AddCard(
            "Show decimal usage",
            "Show one digit after the decimal point for CPU usage.",
            decimalUsageToggle);
    }
}

internal sealed class MemoryProcessColumnPropertiesWindow : ProcessColumnPropertiesWindow
{
    private const double WindowHeight = 410;
    private const double ControlWidth = 190;

    private readonly SettingsComboBox _unitComboBox;
    private readonly TextBox _suffixTextBox;
    private bool _isSynchronizingControls;

    public MemoryProcessColumnPropertiesWindow(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        Action<ProcessColumnSetting> apply)
        : base(setting, palette, apply)
    {
        Height = WindowHeight;
        MinHeight = WindowHeight;

        _unitComboBox = TrayAppDotNETSettingsUI.ComboBox(palette, ControlWidth);
        AddUnit(ProcessMemoryUnit.Kilobytes, "Kilobytes");
        AddUnit(ProcessMemoryUnit.Megabytes, "Megabytes");
        AddUnit(ProcessMemoryUnit.Gigabytes, "Gigabytes");
        AddUnit(ProcessMemoryUnit.PercentageOfSystem, "Percentage of system memory");
        SelectUnit(Setting.MemoryUnit);
        _unitComboBox.SelectionChanged += OnUnitSelectionChanged;
        Own(_unitComboBox);
        AddCard(
            "Memory unit",
            "Choose the divisor used to display memory values.",
            _unitComboBox);

        _suffixTextBox = TrayAppDotNETSettingsUI.TextBox(palette, ControlWidth, Setting.MemorySuffix);
        _suffixTextBox.TextChanged += OnSuffixTextChanged;
        AddCard(
            "Memory suffix",
            "Selecting a memory unit resets this value to its default suffix.",
            _suffixTextBox);
    }

    private void AddUnit(ProcessMemoryUnit unit, string label) =>
        _unitComboBox.Items.Add(new SettingsComboBoxItem(unit, label, Palette));

    private void SelectUnit(ProcessMemoryUnit unit)
    {
        foreach (SettingsComboBoxItem item in _unitComboBox.Items)
        {
            if (item.Tag is not ProcessMemoryUnit itemUnit || itemUnit != unit) continue;
            _unitComboBox.SelectedItem = item;
            return;
        }
    }

    private void OnUnitSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_isSynchronizingControls || _unitComboBox.SelectedItem?.Tag is not ProcessMemoryUnit unit) return;

        string suffix = ProcessColumnSettings.GetDefaultMemorySuffix(unit);
        Setting.MemoryUnit = unit;
        Setting.MemorySuffix = suffix;
        _isSynchronizingControls = true;
        try
        {
            _suffixTextBox.Text = suffix;
        }
        finally
        {
            _isSynchronizingControls = false;
        }

        Publish();
    }

    private void OnSuffixTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        if (_isSynchronizingControls) return;

        Setting.MemorySuffix = _suffixTextBox.Text ?? string.Empty;
        Publish();
    }
}

internal sealed class UserNameProcessColumnPropertiesWindow : ProcessColumnPropertiesWindow
{
    private const double WindowHeight = 330;

    public UserNameProcessColumnPropertiesWindow(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        Action<ProcessColumnSetting> apply)
        : base(setting, palette, apply)
    {
        Height = WindowHeight;
        MinHeight = WindowHeight;

        SettingsToggle prefixToggle = TrayAppDotNETSettingsUI.Toggle(
            palette,
            Setting.ShowUserNamePrefix,
            (_, isChecked) =>
            {
                Setting.ShowUserNamePrefix = isChecked;
                Publish();
            });
        AddCard(
            "Show account prefix",
            "Include the domain or authority before the account name.",
            prefixToggle);
    }
}
