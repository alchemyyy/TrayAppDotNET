using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Base window for live editing of one Processes column's display properties.</summary>
internal abstract class ProcessColumnPropertiesWindow : Window, IDisposable
{
    private const double WindowWidth = 460;
    private const double DefaultWindowHeight = 280;
    private const double ControlWidth = 190;
    private const double ContentPadding = 16;
    private const double TitleBarHeight = 32;
    private const double TitleFontSize = 13;
    private const double RootCornerRadius = 8;

    private readonly List<IDisposable> _ownedControls = [];
    private readonly TextBox _nicknameTextBox;
    private readonly Grid _titleBar;
    private readonly TrayAppDotNETCaptionCloseButton _closeButton;
    private Action<ProcessColumnSetting>? _apply;
    private int _disposed;

    protected ProcessColumnPropertiesWindow(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        bool enableRoundedCorners,
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
        MinWidth = WindowWidth;
        MaxWidth = WindowWidth;
        SetFixedHeight(DefaultWindowHeight);
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        ContentStack = new StackPanel();
        _nicknameTextBox = TrayAppDotNETSettingsUI.TextBox(palette, ControlWidth, Setting.Nickname);
        _nicknameTextBox.PlaceholderText = definition.Title;
        _nicknameTextBox.TextChanged += OnNicknameTextChanged;
        AddCard(
            "Column nickname",
            "Leave blank to use the original column name.",
            _nicknameTextBox);

        _closeButton = new TrayAppDotNETCaptionCloseButton(palette);
        _closeButton.Click += OnCloseClick;
        TrayAppDotNETToolTip.SetTip(_closeButton, "Close");
        TrayAppDotNETToolTip.SuppressWhileEngaged(_closeButton);

        _titleBar = BuildTitleBar(definition.Title, palette, _closeButton);
        _titleBar.PointerPressed += OnTitleBarPointerPressed;

        Content = BuildRoot(palette, enableRoundedCorners, _titleBar, ContentStack);
        KeyDown += OnWindowKeyDown;
        Closed += OnClosed;
    }

    protected ProcessColumnSetting Setting { get; }

    protected SettingsPalette Palette { get; }

    protected StackPanel ContentStack { get; }

    /// <summary>Creates the property-window specialization appropriate for a column.</summary>
    public static ProcessColumnPropertiesWindow Create(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        bool enableRoundedCorners,
        Action<ProcessColumnSetting> apply)
    {
        ArgumentNullException.ThrowIfNull(setting);

        return setting.Column switch
        {
            ProcessTableColumnKind.CPU or ProcessTableColumnKind.CPUUtility =>
                new CPUProcessColumnPropertiesWindow(setting, palette, enableRoundedCorners, apply),
            ProcessTableColumnKind.UserName =>
                new UserNameProcessColumnPropertiesWindow(setting, palette, enableRoundedCorners, apply),
            _ when ProcessColumnSettings.IsMemoryColumn(setting.Column) =>
                new MemoryProcessColumnPropertiesWindow(setting, palette, enableRoundedCorners, apply),
            _ => new DefaultProcessColumnPropertiesWindow(setting, palette, enableRoundedCorners, apply)
        };
    }

    protected void AddCard(string title, string description, Control control) =>
        ContentStack.Children.Add(TrayAppDotNETSettingsUI.Card(title, description, control, Palette));

    protected void Own(IDisposable control) => _ownedControls.Add(control);

    protected void SetFixedHeight(double height)
    {
        Height = height;
        MinHeight = height;
        MaxHeight = height;
    }

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

    private static Border BuildRoot(
        SettingsPalette palette,
        bool enableRoundedCorners,
        Grid titleBar,
        StackPanel contentStack)
    {
        Border body = new()
        {
            Padding = new Thickness(ContentPadding),
            Child = contentStack
        };

        Grid chrome = new();
        chrome.RowDefinitions.Add(new RowDefinition(new GridLength(TitleBarHeight)));
        chrome.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        chrome.Children.Add(titleBar);
        Grid.SetRow(body, 1);
        chrome.Children.Add(body);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = enableRoundedCorners
                ? new CornerRadius(RootCornerRadius)
                : default,
            Child = chrome
        };
    }

    private static Grid BuildTitleBar(
        string title,
        SettingsPalette palette,
        TrayAppDotNETCaptionCloseButton closeButton)
    {
        Grid titleBar = new() { Background = Brushes.Transparent };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock titleText = TrayAppDotNETSettingsUI.Text(
            title,
            palette,
            TitleFontSize,
            FontWeight.SemiBold);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = new Thickness(12, 0, 8, 0);
        titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        titleBar.Children.Add(titleText);

        Grid.SetColumn(closeButton, 1);
        titleBar.Children.Add(closeButton);
        return titleBar;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!eventArgs.GetCurrentPoint(_titleBar).Properties.IsLeftButtonPressed) return;

        BeginMoveDrag(eventArgs);
        eventArgs.Handled = true;
    }

    private void OnCloseClick(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) == 0) Close();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0 || eventArgs.Key != Key.Escape) return;

        Close();
        eventArgs.Handled = true;
    }

    private void OnClosed(object? sender, EventArgs eventArgs) => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Closed -= OnClosed;
        KeyDown -= OnWindowKeyDown;
        _titleBar.PointerPressed -= OnTitleBarPointerPressed;
        _closeButton.Click -= OnCloseClick;
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
        bool enableRoundedCorners,
        Action<ProcessColumnSetting> apply)
        : base(setting, palette, enableRoundedCorners, apply)
    {
    }
}

internal sealed class CPUProcessColumnPropertiesWindow : ProcessColumnPropertiesWindow
{
    private const double WindowHeight = 410;

    public CPUProcessColumnPropertiesWindow(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        bool enableRoundedCorners,
        Action<ProcessColumnSetting> apply)
        : base(setting, palette, enableRoundedCorners, apply)
    {
        SetFixedHeight(WindowHeight);

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
        bool enableRoundedCorners,
        Action<ProcessColumnSetting> apply)
        : base(setting, palette, enableRoundedCorners, apply)
    {
        SetFixedHeight(WindowHeight);

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
        bool enableRoundedCorners,
        Action<ProcessColumnSetting> apply)
        : base(setting, palette, enableRoundedCorners, apply)
    {
        SetFixedHeight(WindowHeight);

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
