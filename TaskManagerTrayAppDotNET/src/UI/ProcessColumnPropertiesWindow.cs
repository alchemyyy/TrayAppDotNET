using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Base window for live editing of one Processes column's display properties.</summary>
internal abstract class ProcessColumnPropertiesWindow : Window, IDisposable
{
    private readonly List<IDisposable> _ownedControls = [];
    private readonly TextBox _nicknameTextBox;
    private readonly Grid _titleBar;
    private readonly TrayAppDotNETCaptionCloseButton _closeButton;
#if DEBUG
    private readonly bool _enableRoundedCorners;
    private readonly Border _rootBorder;
    private readonly Border _body;
    private readonly Grid _chrome;
    private readonly TextBlock _titleText;
#endif
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
            throw new ArgumentOutOfRangeException(nameof(setting), message: "The column kind is not defined.");

        Setting = ProcessColumnSettings.Clone(setting);
        Palette = palette;
        WindowResources = TaskManagerWindowResources.Current;
#if DEBUG
        _enableRoundedCorners = enableRoundedCorners;
#endif
        _apply = apply;

        ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
        Title = definition.Title;
        Width = WindowResources.AxamlProcessColumnProperties.WindowWidth;
        MinWidth = WindowResources.AxamlProcessColumnProperties.WindowWidth;
        MaxWidth = WindowResources.AxamlProcessColumnProperties.WindowWidth;
        SetFixedHeight(WindowResources.AxamlProcessColumnProperties.DefaultWindowHeight);
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        ContentStack = new StackPanel();
        _nicknameTextBox = TrayAppDotNETSettingsUI.TextBox(
            palette,
            WindowResources.AxamlProcessColumnProperties.ControlWidth,
            Setting.Nickname);
        _nicknameTextBox.PlaceholderText = definition.Title;
        _nicknameTextBox.TextChanged += OnNicknameTextChanged;
        AddCard(
            title: "Column nickname",
            description: "Leave blank to use the original column name.",
            _nicknameTextBox);

        if (ProcessColumnSettings.SupportsLiveTotal(Setting.Column))
        {
            SetFixedHeight(WindowResources.AxamlProcessColumnProperties.LiveTotalWindowHeight);
            SettingsToggle liveTotalToggle = TrayAppDotNETSettingsUI.Toggle(
                palette,
                Setting.ShowLiveTotal,
                (_, isChecked) =>
                {
                    Setting.ShowLiveTotal = isChecked;
                    Publish();
                });
            AddCard(
                title: "Show live total",
                description: "Show the live aggregate for all processes before the column name.",
                liveTotalToggle);
        }

        _closeButton = new TrayAppDotNETCaptionCloseButton(palette);
        _closeButton.Click += OnCloseClick;
        TrayAppDotNETToolTip.SetTip(_closeButton, tip: "Close");
        TrayAppDotNETToolTip.SuppressWhileEngaged(_closeButton);

        _titleBar = BuildTitleBar(definition.Title, palette, WindowResources, _closeButton);
        _titleBar.PointerPressed += OnTitleBarPointerPressed;

        Border rootBorder = BuildRoot(
            palette,
            enableRoundedCorners,
            WindowResources,
            _titleBar,
            ContentStack);
        Content = rootBorder;
#if DEBUG
        _rootBorder = rootBorder;
        _chrome = (Grid)rootBorder.Child!;
        _body = (Border)_chrome.Children[1];
        _titleText = (TextBlock)_titleBar.Children[0];
#endif
        KeyDown += OnWindowKeyDown;
        Closed += OnClosed;
    }

    protected ProcessColumnSetting Setting { get; }

    protected SettingsPalette Palette { get; }

    protected TaskManagerWindowResources WindowResources { get; }

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
            ProcessTableColumnKind.CPU
                or ProcessTableColumnKind.CPUSingle
                or ProcessTableColumnKind.CPUUtility =>
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

#if DEBUG
    /// <summary>Applies current AXAML metrics without replacing the editor's live controls.</summary>
    internal void ApplyAXAMLResources(IReadOnlyList<ProcessColumnSetting> currentSettings)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        ArgumentNullException.ThrowIfNull(currentSettings);

        for (int settingIndex = 0; settingIndex < currentSettings.Count; settingIndex++)
        {
            ProcessColumnSetting currentSetting = currentSettings[settingIndex];
            if (currentSetting.Column != Setting.Column) continue;

            Setting.Width = currentSetting.Width;
            break;
        }

        double width = WindowResources.AxamlProcessColumnProperties.WindowWidth;
        Width = width;
        MinWidth = width;
        MaxWidth = width;
        SetFixedHeight(ResolveWindowHeight(WindowResources, Setting.Column));
        _nicknameTextBox.Width = WindowResources.AxamlProcessColumnProperties.ControlWidth;
        _body.Padding = WindowResources.AxamlProcessColumnProperties.ContentPadding;
        _chrome.RowDefinitions[0].Height = new GridLength(
            WindowResources.AxamlProcessColumnProperties.TitleBarHeight);
        _rootBorder.BorderThickness = WindowResources.AxamlProcessColumnProperties.RootBorderThickness;
        _rootBorder.CornerRadius = _enableRoundedCorners
            ? WindowResources.AxamlProcessColumnProperties.RootCornerRadius
            : default;
        _titleText.FontSize = WindowResources.AxamlProcessColumnProperties.TitleFontSize;
        _titleText.FontWeight = (FontWeight)WindowResources.AxamlProcessColumnProperties.TitleFontWeight;
        _titleText.Margin = WindowResources.AxamlProcessColumnProperties.TitleMargin;
        ApplySpecializedAXAMLResources(WindowResources);
    }

    protected virtual void ApplySpecializedAXAMLResources(TaskManagerWindowResources resources)
    {
    }

    private static double ResolveWindowHeight(
        TaskManagerWindowResources resources,
        ProcessTableColumnKind column) =>
        column switch
        {
            ProcessTableColumnKind.CPU
                or ProcessTableColumnKind.CPUSingle
                or ProcessTableColumnKind.CPUUtility =>
                resources.AxamlProcessColumnProperties.CPUWindowHeight,
            ProcessTableColumnKind.UserName =>
                resources.AxamlProcessColumnProperties.UserNameWindowHeight,
            _ when ProcessColumnSettings.IsMemoryColumn(column) =>
                resources.AxamlProcessColumnProperties.MemoryWindowHeight,
            _ when ProcessColumnSettings.SupportsLiveTotal(column) =>
                resources.AxamlProcessColumnProperties.LiveTotalWindowHeight,
            _ => resources.AxamlProcessColumnProperties.DefaultWindowHeight
        };
#endif

    private void OnNicknameTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        Setting.Nickname = _nicknameTextBox.Text ?? string.Empty;
        Publish();
    }

    private static Border BuildRoot(
        SettingsPalette palette,
        bool enableRoundedCorners,
        TaskManagerWindowResources resources,
        Grid titleBar,
        StackPanel contentStack)
    {
        Border body = new() { Padding = resources.AxamlProcessColumnProperties.ContentPadding, Child = contentStack };

        Grid chrome = new();
        chrome.RowDefinitions.Add(new RowDefinition(
            new GridLength(resources.AxamlProcessColumnProperties.TitleBarHeight)));
        chrome.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        chrome.Children.Add(titleBar);
        Grid.SetRow(body, value: 1);
        chrome.Children.Add(body);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = resources.AxamlProcessColumnProperties.RootBorderThickness,
            CornerRadius = enableRoundedCorners
                ? resources.AxamlProcessColumnProperties.RootCornerRadius
                : default,
            Child = chrome
        };
    }

    private static Grid BuildTitleBar(
        string title,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        TrayAppDotNETCaptionCloseButton closeButton)
    {
        Grid titleBar = new() { Background = Brushes.Transparent };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock titleText = TrayAppDotNETSettingsUI.Text(
            title,
            palette,
            resources.AxamlProcessColumnProperties.TitleFontSize,
            (FontWeight)resources.AxamlProcessColumnProperties.TitleFontWeight);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = resources.AxamlProcessColumnProperties.TitleMargin;
        titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        titleBar.Children.Add(titleText);

        Grid.SetColumn(closeButton, value: 1);
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
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

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

internal sealed class DefaultProcessColumnPropertiesWindow(
    ProcessColumnSetting setting,
    SettingsPalette palette,
    bool enableRoundedCorners,
    Action<ProcessColumnSetting> apply)
    : ProcessColumnPropertiesWindow(setting, palette, enableRoundedCorners, apply);

internal sealed class CPUProcessColumnPropertiesWindow : ProcessColumnPropertiesWindow
{
    public CPUProcessColumnPropertiesWindow(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        bool enableRoundedCorners,
        Action<ProcessColumnSetting> apply)
        : base(setting, palette, enableRoundedCorners, apply)
    {
        SetFixedHeight(WindowResources.AxamlProcessColumnProperties.CPUWindowHeight);

        SettingsToggle percentSuffixToggle = TrayAppDotNETSettingsUI.Toggle(
            palette,
            Setting.ShowPercentSuffix,
            (_, isChecked) =>
            {
                Setting.ShowPercentSuffix = isChecked;
                Publish();
            });
        AddCard(
            title: "Show % suffix",
            description: "Append a percent sign to CPU usage values.",
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
            title: "Show decimal usage",
            description: "Show one digit after the decimal point for CPU usage.",
            decimalUsageToggle);
    }
}

internal sealed class MemoryProcessColumnPropertiesWindow : ProcessColumnPropertiesWindow
{
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
        SetFixedHeight(WindowResources.AxamlProcessColumnProperties.MemoryWindowHeight);

        _unitComboBox = TrayAppDotNETSettingsUI.ComboBox(
            palette,
            WindowResources.AxamlProcessColumnProperties.ControlWidth);
        AddUnit(ProcessMemoryUnit.Kilobytes, label: "Kilobytes");
        AddUnit(ProcessMemoryUnit.Megabytes, label: "Megabytes");
        AddUnit(ProcessMemoryUnit.Gigabytes, label: "Gigabytes");
        AddUnit(ProcessMemoryUnit.PercentageOfSystem, label: "Percentage of system memory");
        SelectUnit(Setting.MemoryUnit);
        _unitComboBox.SelectionChanged += OnUnitSelectionChanged;
        Own(_unitComboBox);
        AddCard(
            title: "Memory unit",
            description: "Choose the divisor used to display memory values.",
            _unitComboBox);

        _suffixTextBox = TrayAppDotNETSettingsUI.TextBox(
            palette,
            WindowResources.AxamlProcessColumnProperties.ControlWidth,
            Setting.MemorySuffix);
        _suffixTextBox.TextChanged += OnSuffixTextChanged;
        AddCard(
            title: "Memory suffix",
            description: "Selecting a memory unit resets this value to its default suffix.",
            _suffixTextBox);
    }

    private void AddUnit(ProcessMemoryUnit unit, string label) =>
        _unitComboBox.Items.Add(new SettingsComboBoxItem(unit, label, Palette));

#if DEBUG
    protected override void ApplySpecializedAXAMLResources(TaskManagerWindowResources resources)
    {
        _unitComboBox.Width = resources.AxamlProcessColumnProperties.ControlWidth;
        _suffixTextBox.Width = resources.AxamlProcessColumnProperties.ControlWidth;
    }
#endif

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
    public UserNameProcessColumnPropertiesWindow(
        ProcessColumnSetting setting,
        SettingsPalette palette,
        bool enableRoundedCorners,
        Action<ProcessColumnSetting> apply)
        : base(setting, palette, enableRoundedCorners, apply)
    {
        SetFixedHeight(WindowResources.AxamlProcessColumnProperties.UserNameWindowHeight);

        SettingsToggle prefixToggle = TrayAppDotNETSettingsUI.Toggle(
            palette,
            Setting.ShowUserNamePrefix,
            (_, isChecked) =>
            {
                Setting.ShowUserNamePrefix = isChecked;
                Publish();
            });
        AddCard(
            title: "Show account prefix",
            description: "Include the domain or authority before the account name.",
            prefixToggle);
    }
}
