using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FanControlTrayAppDotNET.UI.Curves;
using FanControlTrayAppDotNET.UI.Settings;
using TrayAppDotNETCommon.UI;
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;

namespace FanControlTrayAppDotNET.UI.Flyout;

public sealed partial class FanPropertiesWindow : Window
{
    private const int DutyCycleMinimum = 0;
    private const int DutyCycleMaximum = 100;
    private const int FallbackMaximumRPM = 3000;
    private const string DutyCycleSuffix = "%";
    private const string RPMSuffix = "RPM";
    private const string DutyCycleRateSuffix = "%/s";
    private const string RPMRateSuffix = "RPM/s";

    private readonly Fan _fan;
    private readonly AppSettings _settings;
    private readonly SettingsPalette _palette;
    private FanPropertiesAxamlProperties? _layout;
    private readonly TextBlock _titleText;
    private readonly TextBlock _fanIDText;
    private readonly TextBlock _sensorControllerText;
    private readonly TextBox _nameBox;
    private readonly SettingsComboBox _groupCombo;
    private readonly SettingsComboBox _curveCombo;
    private readonly RadioButton _curveModeRadio;
    private readonly RadioButton _manualModeRadio;
    private readonly RadioButton _detachedModeRadio;
    private readonly SettingsNumberBox _jumpstartBox;
    private readonly SettingsNumberBox _clampHighBox;
    private readonly SettingsNumberBox _clampLowBox;
    private readonly SettingsNumberBox _warnLowBox;
    private readonly SettingsNumberBox _warnHighBox;
    private readonly SettingsNumberBox _deltaMaxBox;
    private readonly SettingsNumberBox _offsetBox;
    private readonly List<FanPropertyUnitBinding> _propertyUnitBindings = [];
    private readonly SettingsButton _editCurveButton;
    private readonly SettingsButton _pinButton;
    private readonly SettingsButton _closeButton;
    private readonly ControlNameScope _controlNames;
    private readonly List<FanCurveEditorWindow> _curveEditorWindows = [];
    private readonly Dictionary<FanCurveEditorWindow, UIResourceScope> _curveEditorSubscriptionResources = [];
    private readonly UIResourceScope _windowResources = new(nameof(FanPropertiesWindow));
    private bool _forceClose;
    private bool _isUpdatingPropertyUnitControls;

    public FanPropertiesWindow()
    {
        _controlNames = ControlNameScope.For(this);
        _fan = null!;
        _settings = null!;
        _palette = null!;
        _titleText = null!;
        _fanIDText = null!;
        _sensorControllerText = null!;
        _nameBox = null!;
        _groupCombo = null!;
        _curveCombo = null!;
        _curveModeRadio = null!;
        _manualModeRadio = null!;
        _detachedModeRadio = null!;
        _jumpstartBox = null!;
        _clampHighBox = null!;
        _clampLowBox = null!;
        _warnLowBox = null!;
        _warnHighBox = null!;
        _deltaMaxBox = null!;
        _offsetBox = null!;
        _editCurveButton = null!;
        _pinButton = null!;
        _closeButton = null!;

        InitializeComponent();
        InitializeComponentState();
    }

    public FanPropertiesWindow(Fan fan, AppSettings settings)
    {
        _controlNames = ControlNameScope.For(this);
        _fan = fan;
        _settings = settings;

        try
        {
            InitializeComponent();
            InitializeComponentState();

            _palette = FanSettingsWindow.CreatePalette(
                AppServices.Theme,
                _settings,
                AppTheme.ResolveEffectiveIsLightTheme(_settings));
            SettingsPalette palette = _palette;
            bool rounded = _settings.EnableRoundedCorners;

            _titleText = ControlNames.Assign(
                TrayAppDotNETSettingsUI.Text(
                    "Fan Properties",
                    palette,
                    Layout.TitleFontSize,
                    FontWeight.SemiBold),
                "TitleBar");
            _fanIDText = ControlNames.Assign(ValueText(palette), "FanID");
            _sensorControllerText = ControlNames.Assign(ValueText(palette), "SensorController");
            _nameBox = ControlNames.Assign(
                TrayAppDotNETSettingsUI.TextBox(palette, Layout.TextBoxWidth),
                "FanName");
            _groupCombo = _windowResources.Own(
                ControlNames.Assign(
                    TrayAppDotNETSettingsUI.ComboBox(
                        palette,
                        Layout.TextBoxWidth,
                        autoSizeToText: true),
                    "FanGroup"));
            _curveCombo = _windowResources.Own(
                ControlNames.Assign(
                    TrayAppDotNETSettingsUI.ComboBox(
                        palette,
                        Layout.CurveComboBoxWidth,
                        autoSizeToText: true),
                    "FanCurve"));
            _curveCombo.SelectionChanged += (_, _) => RefreshPropertyUnitControls();
            _curveModeRadio = ControlNames.Assign(CompactRadio("Curve", palette), "FanMode");
            _manualModeRadio = ControlNames.Assign(CompactRadio("Manual", palette), "FanMode");
            _detachedModeRadio = ControlNames.Assign(CompactRadio("Detached", palette), "FanMode");
            _jumpstartBox = _windowResources.Own(
                ControlNames.Assign(
                    Number(palette, DutyCycleMinimum, DutyCycleMaximum, DutyCycleSuffix),
                    "Jumpstart"));
            _clampHighBox = _windowResources.Own(
                ControlNames.Assign(
                    Number(palette, DutyCycleMinimum, DutyCycleMaximum, DutyCycleSuffix),
                    "ClampHigh"));
            _clampLowBox = _windowResources.Own(
                ControlNames.Assign(
                    Number(palette, DutyCycleMinimum, DutyCycleMaximum, DutyCycleSuffix),
                    "ClampLow"));
            _warnLowBox = _windowResources.Own(
                ControlNames.Assign(
                    Number(palette, DutyCycleMinimum, DutyCycleMaximum, DutyCycleSuffix),
                    "WarnLow"));
            _warnHighBox = _windowResources.Own(
                ControlNames.Assign(
                    Number(palette, DutyCycleMinimum, DutyCycleMaximum, DutyCycleSuffix),
                    "WarnHigh"));
            _deltaMaxBox = _windowResources.Own(
                ControlNames.Assign(
                    Number(palette, DutyCycleMinimum, DutyCycleMaximum, DutyCycleRateSuffix),
                    "DeltaMax"));
            _offsetBox = _windowResources.Own(
                ControlNames.Assign(
                    Number(palette, -DutyCycleMaximum, DutyCycleMaximum, DutyCycleSuffix),
                    "Offset"));
            _editCurveButton = ControlNames.Assign(
                TrayAppDotNETSettingsUI.Button("Edit curve", palette),
                "FanCurve");

            _pinButton = ControlNames.Assign(CaptionButton(GlyphCatalog.PIN, palette), "TitleBar");
            _closeButton = ControlNames.Assign(CaptionButton(GlyphCatalog.EXIT, palette), "TitleBar");
            _pinButton.Click += (_, _) => IsPinned = !IsPinned;
            _closeButton.Click += (_, _) => RequestClose();

            Grid titleBar = ControlNames.Assign(
                BuildTitleBar(palette, _pinButton, _closeButton),
                "Chrome");
            Grid body = ControlNames.Assign(BuildBody(palette), "Chrome");
            Grid footer = ControlNames.Assign(BuildFooter(palette), "Chrome");

            Grid chrome = ControlNames.Assign(new Grid(), nameof(FanPropertiesWindow));
            chrome.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.TitleBarHeight)));
            chrome.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            chrome.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.FooterHeight)));
            chrome.Children.Add(titleBar);
            Grid.SetRow(body, 1);
            chrome.Children.Add(body);
            Grid.SetRow(footer, 2);
            chrome.Children.Add(footer);

            Border root = ControlNames.Assign(
                new Border
                {
                    Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
                    BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
                    BorderThickness = Layout.RootBorderThickness,
                    CornerRadius = rounded ? Layout.RootCornerRadius : Layout.ZeroCornerRadius,
                    Child = chrome
                },
                nameof(FanPropertiesWindow));
            ControlNames.AssignLogicalSubtree(root, this);
            Content = root;

            LoadFromFan();
            _fan.PropertyChanged += OnFanPropertyChanged;
            _windowResources.Add(() => _fan.PropertyChanged -= OnFanPropertyChanged);
            _settings.Changed += OnSettingsChanged;
            _windowResources.Add(() => _settings.Changed -= OnSettingsChanged);
            GlyphCatalogHotReload.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
            _windowResources.Add(() =>
                GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded);
        }
        catch
        {
            _windowResources.Dispose();
            throw;
        }
    }

    private void InitializeComponentState()
    {
        _layout = AxamlFanProperties;
    }

    private ControlNameScope ControlNames => _controlNames;

    /// <summary>
    /// Reapplies glyph metadata to the persistent caption buttons.
    /// </summary>
    private void OnGlyphCatalogResourcesReloaded()
    {
        if (_windowResources.IsDisposed) return;

        _pinButton.Label.RenderTransform = null;
        _closeButton.Label.RenderTransform = null;
        GlyphApplicator.ApplyTo(_pinButton.Label, IsPinned ? GlyphCatalog.PINNED : GlyphCatalog.PIN);
        GlyphApplicator.ApplyTo(_closeButton.Label, GlyphCatalog.EXIT);
    }

    private FanPropertiesAxamlProperties Layout =>
        _layout ?? throw new InvalidOperationException("Fan properties layout resources have not been loaded.");

    private Thickness RowMargin(double bottom) =>
        new(Layout.ZeroThickness.Left, Layout.ZeroThickness.Top, Layout.ZeroThickness.Right, bottom);

    public bool IsPinned
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            GlyphApplicator.ApplyTo(_pinButton.Label, value ? GlyphCatalog.PINNED : GlyphCatalog.PIN);
        }
    }

    public bool HasFocus() => IsActive;

    public bool RequestClose()
    {
        if (IsPinned)
        {
            if (IsVisible) Activate();
            return false;
        }

        Close();
        return true;
    }

    public void ForceClose()
    {
        _forceClose = true;
        try { Close(); }
        catch (Exception ex)
        {
            TADNLog.Log($"FanPropertiesWindow.ForceClose: {ex.Message}");
        }
    }

    /// <summary>Hides this pinned window after retiring curve editors owned by the visible flyout session.</summary>
    internal void HideForFlyoutDismissal()
    {
        ForceCloseAllCurveEditorWindows();
        Hide();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (ShouldCancelPinnedClose(_forceClose, IsPinned, e.CloseReason))
        {
            e.Cancel = true;
            if (!IsVisible && Owner is Window owner)
                Show(owner);
            Activate();
            return;
        }

        base.OnClosing(e);
    }

    internal static bool ShouldCancelPinnedClose(
        bool forceClose,
        bool isPinned,
        WindowCloseReason closeReason) =>
        !forceClose
        && isPinned
        && closeReason is not (WindowCloseReason.OwnerWindowClosing
            or WindowCloseReason.ApplicationShutdown
            or WindowCloseReason.OSShutdown);

    private Grid BuildTitleBar(SettingsPalette p, SettingsButton pinButton, SettingsButton closeButton)
    {
        Grid titleBar = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(
                (AppServices.Theme ?? AppTheme.Default).ResolveFlyoutTitleBarBackground(_settings,
                    AppTheme.ResolveEffectiveIsLightTheme(_settings)))
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.Source is SettingsButton) return;
            if (e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _titleText.Margin = Layout.TitleMargin;
        _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        titleBar.Children.Add(_titleText);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { pinButton, closeButton }
        };
        Grid.SetColumn(buttons, 1);
        titleBar.Children.Add(buttons);
        return titleBar;
    }

    private Grid BuildBody(SettingsPalette p)
    {
        Grid body = new() { Margin = Layout.BodyMargin };
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.BodyLeftColumnWidth)));
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        StackPanel left = new() { Margin = Layout.LeftMargin };
        left.Children.Add(Row("ID", _fanIDText, p));
        left.Children.Add(Row("Sensor", _sensorControllerText, p, bottomMargin: 6));
        left.Children.Add(Row("Name", _nameBox, p));
        left.Children.Add(Row("Group", _groupCombo, p));
        left.Children.Add(Row("Mode",
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _curveModeRadio, _manualModeRadio, _detachedModeRadio }
            }, p));
        left.Children.Add(RPMModeHeaderRow(p));
        left.Children.Add(NumberRow("Jumpstart", _jumpstartBox, FanPropertyUnitKind.StartupSpeed, p));
        left.Children.Add(NumberRow("Max Duty", _clampHighBox, FanPropertyUnitKind.ClampHigh, p));
        left.Children.Add(NumberRow("Min Duty", _clampLowBox, FanPropertyUnitKind.ClampLow, p));
        left.Children.Add(NumberRow("Warn Low", _warnLowBox, FanPropertyUnitKind.WarnLow, p));
        left.Children.Add(NumberRow("Warn High", _warnHighBox, FanPropertyUnitKind.WarnHigh, p));
        left.Children.Add(NumberRow("Max Delta", _deltaMaxBox, FanPropertyUnitKind.DeltaMax, p));
        left.Children.Add(NumberRow("Offset", _offsetBox, FanPropertyUnitKind.Offset, p,
            bottomMargin: Layout.OffsetRowBottomMargin));

        ScrollViewer scroll = new()
        {
            Content = left,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        body.Children.Add(scroll);

        Grid right = new() { Margin = Layout.RightMargin };
        right.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.RightPreviewHeight)));
        right.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        right.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        right.Children.Add(new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.ControlBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = _settings.EnableRoundedCorners ? Layout.InnerCornerRadius : Layout.ZeroCornerRadius
        });
        _curveCombo.Margin = Layout.CurveComboBoxMargin;
        Grid.SetRow(_curveCombo, 1);
        right.Children.Add(_curveCombo);
        _editCurveButton.Margin = Layout.EditCurveButtonMargin;
        _editCurveButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _editCurveButton.Click += (_, _) => OpenCurveEditor();
        Grid.SetRow(_editCurveButton, 2);
        right.Children.Add(_editCurveButton);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        return body;
    }

    private Grid BuildFooter(SettingsPalette p)
    {
        Grid footer = new() { Margin = Layout.FooterMargin };
        SettingsButton reset = TrayAppDotNETSettingsUI.Button("Reset to defaults", p);
        SettingsButton save = TrayAppDotNETSettingsUI.Button("Save", p);
        reset.Margin = Layout.ResetButtonMargin;
        reset.Click += (_, _) => ResetToDefaults();
        save.Click += (_, _) => SaveFromControls();

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { reset, save }
        };
        footer.Children.Add(buttons);
        return footer;
    }

    private void LoadFromFan()
    {
        UpdateTitle();
        _fanIDText.Text = string.IsNullOrWhiteSpace(_fan.DataSourceKey) ? _fan.FansName : _fan.DataSourceKey;
        _sensorControllerText.Text = _fan.ControllerDisplayLabel;
        _nameBox.Text = _fan.UserDefinedName;
        PopulateGroupCombo();
        PopulateCurveCombo();
        SelectComboByTag(_groupCombo, _fan.Group ?? string.Empty);
        SelectComboByTag(_curveCombo, GetEffectiveCurveName(_fan));
        _detachedModeRadio.IsChecked = _fan.ForcedNonFunctioning;
        _curveModeRadio.IsChecked = _fan is { ForcedNonFunctioning: false, CurrentControlMode: FanControlMode.Curve };
        _manualModeRadio.IsChecked = _fan is { ForcedNonFunctioning: false, CurrentControlMode: FanControlMode.Manual };
        LoadPropertyUnitControls();
        _jumpstartBox.Value = _fan.StartupSpeed;
        _clampHighBox.Value = _fan.ClampHigh;
        _clampLowBox.Value = _fan.ClampLow;
        _warnLowBox.Value = _fan.WarnLow;
        _warnHighBox.Value = _fan.WarnHigh;
        _deltaMaxBox.Value = _fan.DeltaMax;
        _offsetBox.Value = _fan.Offset;
    }

    private void PopulateGroupCombo()
    {
        _groupCombo.Items.Clear();
        _groupCombo.Items.Add(new SettingsComboBoxItem(string.Empty, "None", Palette()));

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (FanGroup group in _settings.FanGroups
                     .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                     .OrderBy(g => g.DisplayOrder)
                     .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            string groupName = group.Name!;
            if (!names.Add(groupName)) continue;
            _groupCombo.Items.Add(new SettingsComboBoxItem(groupName, groupName, Palette()));
        }

        foreach (FanGroup group in FanGroup.FanGroups.Values
                     .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                     .OrderBy(g => g.DisplayOrder)
                     .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            string groupName = group.Name!;
            if (!names.Add(groupName)) continue;
            _groupCombo.Items.Add(new SettingsComboBoxItem(groupName, groupName, Palette()));
        }
    }

    private void PopulateCurveCombo()
    {
        _curveCombo.Items.Clear();
        _curveCombo.Items.Add(new SettingsComboBoxItem(string.Empty, "None", Palette()));
        foreach (Curve curve in Curve.Curves.Values
                     .Where(c => !string.IsNullOrWhiteSpace(c.CurveName))
                     .OrderBy(c => c.CurveName, StringComparer.OrdinalIgnoreCase))
            _curveCombo.Items.Add(new SettingsComboBoxItem(curve.CurveName, curve.CurveName, Palette()));
    }

    private void SaveFromControls()
    {
        ApplyControlsToFan();
        PersistAndNotify();
        LoadFromFan();
    }

    private void ResetToDefaults()
    {
        _nameBox.Text = string.Empty;
        SelectComboByTag(_groupCombo, string.Empty);
        string defaultCurve = NormalizeCurveName(_settings.DefaultAssignedCurve);
        SelectComboByTag(_curveCombo, defaultCurve);
        _curveModeRadio.IsChecked = !string.IsNullOrEmpty(defaultCurve);
        _manualModeRadio.IsChecked = string.IsNullOrEmpty(defaultCurve);
        _detachedModeRadio.IsChecked = false;
        SetAllPropertyUnitControls(rpmMode: false, convertValues: false);
        _jumpstartBox.Value = _settings.DefaultJumpstartDutyCycle;
        _clampHighBox.Value = 100;
        _clampLowBox.Value = 0;
        _warnLowBox.Value = 0;
        _warnHighBox.Value = 100;
        _deltaMaxBox.Value = _settings.DefaultDeltaMaxDutyCycle;
        _offsetBox.Value = 0;
        SaveFromControls();
    }

    private void ApplyControlsToFan()
    {
        string groupName = SelectedTag(_groupCombo);
        string curveName = SelectedTag(_curveCombo);
        bool clampLowRPMMode = PropertyRPMMode(FanPropertyUnitKind.ClampLow);
        bool clampHighRPMMode = PropertyRPMMode(FanPropertyUnitKind.ClampHigh);
        bool warnLowRPMMode = PropertyRPMMode(FanPropertyUnitKind.WarnLow);
        bool warnHighRPMMode = PropertyRPMMode(FanPropertyUnitKind.WarnHigh);
        (int clampLow, int clampHigh) = NormalizeBoundValues(
            ReadInt(_clampLowBox),
            clampLowRPMMode,
            ReadInt(_clampHighBox),
            clampHighRPMMode);
        (int warnLow, int warnHigh) = NormalizeBoundValues(
            ReadInt(_warnLowBox),
            warnLowRPMMode,
            ReadInt(_warnHighBox),
            warnHighRPMMode);

        _fan.UserDefinedName = (_nameBox.Text ?? string.Empty).Trim();
        _fan.Group = string.IsNullOrWhiteSpace(groupName) ? null : groupName;
        _fan.StartupSpeed = ReadInt(_jumpstartBox);
        _fan.StartupSpeedRPMMode = PropertyRPMMode(FanPropertyUnitKind.StartupSpeed);
        _fan.ClampLow = clampLow;
        _fan.ClampLowRPMMode = clampLowRPMMode;
        _fan.ClampHigh = clampHigh;
        _fan.ClampHighRPMMode = clampHighRPMMode;
        _fan.WarnLow = warnLow;
        _fan.WarnLowRPMMode = warnLowRPMMode;
        _fan.WarnHigh = warnHigh;
        _fan.WarnHighRPMMode = warnHighRPMMode;
        _fan.DeltaMax = ReadInt(_deltaMaxBox);
        _fan.DeltaMaxRPMMode = PropertyRPMMode(FanPropertyUnitKind.DeltaMax);
        _fan.Offset = ReadInt(_offsetBox);
        _fan.OffsetRPMMode = PropertyRPMMode(FanPropertyUnitKind.Offset);

        if (_detachedModeRadio.IsChecked == true)
            _fan.ForcedNonFunctioning = true;
        else
        {
            _fan.ForcedNonFunctioning = false;
            _fan.CurrentControlMode = _curveModeRadio.IsChecked == true
                ? FanControlMode.Curve
                : FanControlMode.Manual;
        }

        ApplyCurveSelection(curveName);
    }

    private void ApplyCurveSelection(string curveName)
    {
        Curve? curve = Curve.Find(curveName);
        IEnumerable<Fan>? liveFans = AppServices.LHMService?.Fans;
        IEnumerable<Fan> fans = liveFans ?? [_fan];
        if (!string.IsNullOrWhiteSpace(_fan.Group) && FanGroup.Find(_fan.Group) is { } group)
        {
            group.AssignedCurveName = curveName;
            FanCurveModeSync.ApplyToGroup(group, fans, curve);
            FanGroup.Register(group);
            return;
        }

        _fan.AssignedCurveName = curveName;
        FanCurveModeSync.ApplyToFan(_fan, curve);
    }

    private void OpenCurveEditor()
    {
        string curveName = SelectedTag(_curveCombo);
        Curve? curve = string.IsNullOrWhiteSpace(curveName) ? null : Curve.Find(curveName);
        if (curve == null)
        {
            curve = CreateCurveForFan();
            curveName = curve.CurveName;
            PopulateCurveCombo();
            SelectComboByTag(_curveCombo, curveName);
        }

        ApplyControlsToFan();
        ApplyCurveSelection(curveName);
        _fan.CurrentControlMode = FanControlMode.Curve;
        _curveModeRadio.IsChecked = true;
        PersistAndNotify();

        FanCurveEditorWindow window = new(_fan, curve, _settings)
        {
            Topmost = Topmost,
            ShowInTaskbar = false
        };
        try
        {
            _curveEditorWindows.Add(window);
            RegisterCurveEditorWindow(window);
            window.Show(this);
        }
        catch
        {
            ReleaseCurveEditorWindowSubscription(window);
            _curveEditorWindows.Remove(window);
            try { window.Close(); }
            catch (Exception exception)
            {
                TADNLog.Log($"FanPropertiesWindow failed curve editor cleanup: {exception.Message}");
            }
            throw;
        }
    }

    /// <summary>
    /// Removes a closed curve editor and refreshes curve selection while this window is alive.
    /// </summary>
    private void OnCurveEditorWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not FanCurveEditorWindow window) return;

        ReleaseCurveEditorWindowSubscription(window);
        _curveEditorWindows.Remove(window);
        if (!IsVisible) return;

        PopulateCurveCombo();
        SelectComboByTag(_curveCombo, GetEffectiveCurveName(_fan));
    }

    /// <summary>
    /// Closes curve editor child windows owned by this properties window.
    /// </summary>
    private void ForceCloseAllCurveEditorWindows()
    {
        foreach (FanCurveEditorWindow window in _curveEditorWindows.ToArray())
        {
            ReleaseCurveEditorWindowSubscription(window);
            try { window.Close(); }
            catch (Exception ex)
            {
                TADNLog.Log($"FanPropertiesWindow curve editor close failed: {ex.Message}");
            }
        }

        _curveEditorWindows.Clear();
        _curveEditorSubscriptionResources.Clear();
    }

    private void RegisterCurveEditorWindow(FanCurveEditorWindow window)
    {
        UIResourceScope resources = _windowResources.CreateChild(
            $"{nameof(FanPropertiesWindow)}.CurveEditorSubscription");
        try
        {
            window.Closed += OnCurveEditorWindowClosed;
            resources.Add(() => window.Closed -= OnCurveEditorWindowClosed);
            _curveEditorSubscriptionResources.Add(window, resources);
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    private void ReleaseCurveEditorWindowSubscription(FanCurveEditorWindow window)
    {
        if (!_curveEditorSubscriptionResources.Remove(window, out UIResourceScope? resources)) return;
        resources.Dispose();
    }

    private Curve CreateCurveForFan()
    {
        string name = UniqueCurveName($"{_fan.DisplayName} Curve");
        int maxRPM = _fan.MaxRPM > 0 ? _fan.MaxRPM : _fan.CurrentRPM > 0 ? Math.Max(100, _fan.CurrentRPM) : 3000;
        Curve curve = new()
        {
            CurveName = name,
            RPMMode = _fan.RPMMode,
            MaxRPM = maxRPM,
            MinRPM = 0,
            MaxDutyCycle = 100,
            MinDutyCycle = 0,
            SmoothingFactor = 50,
            PreventDecreasing = true,
            SelectedDataSourceKey = DefaultCurveDataSourceKey()
        };
        Curve.Register(curve);
        return curve;
    }

    private static string UniqueCurveName(string baseName)
    {
        string normalized = string.IsNullOrWhiteSpace(baseName) ? "Fan Curve" : baseName.Trim();
        string candidate = normalized;
        int suffix = 2;
        while (Curve.Find(candidate) != null)
            candidate = $"{normalized} {suffix++}";
        return candidate;
    }

    private static string DefaultCurveDataSourceKey()
    {
        DataSource? source = DataSource.DataSources.Values
            .OrderByDescending(static s => s.DataSourceType == DataSourceTypeEnum.Temperature)
            .ThenBy(static s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return source?.DataSourceKey ?? string.Empty;
    }

    private void PersistAndNotify()
    {
        AppServices.LHMService?.PersistLiveState(save: false);
        _settings.SyncFanControlRegistriesForSave();
        _settings.Save();
        _settings.RaiseChanged();
    }

    private void OnFanPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _fan) || _windowResources.IsDisposed) return;

        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(Fan.DisplayName)
            || e.PropertyName == nameof(Fan.UserDefinedName))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_windowResources.IsDisposed)
                    UpdateTitle();
            });
        }

        if (e.PropertyName == nameof(Fan.MaxRPM))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_windowResources.IsDisposed)
                    RefreshPropertyUnitControls();
            });
        }
    }

    private void OnSettingsChanged()
    {
        if (_windowResources.IsDisposed) return;

        _palette.UpdateFrom(Palette());
        if (Content is Border root)
            root.CornerRadius = _settings.EnableRoundedCorners ? Layout.RootCornerRadius : Layout.ZeroCornerRadius;
    }

    private void UpdateTitle()
    {
        string title = $"Fan Properties: {_fan.DisplayName}";
        Title = title;
        _titleText.Text = title;
    }

    protected override void OnClosed(EventArgs e)
    {
        // Detach model publishers before any child-window close can fail
        _windowResources.Dispose();
        ForceCloseAllCurveEditorWindows();
        base.OnClosed(e);
    }

    private SettingsPalette Palette() =>
        FanSettingsWindow.CreatePalette(AppServices.Theme, _settings, AppTheme.ResolveEffectiveIsLightTheme(_settings));

    private SettingsNumberBox Number(SettingsPalette p, int min, int max, string suffix) =>
        TrayAppDotNETSettingsUI.NumberBox(p, 0, min, max, Layout.NumberBoxWidth, suffix);

    private Grid RPMModeHeaderRow(SettingsPalette p)
    {
        TextBlock header = TrayAppDotNETSettingsUI.Text("RPM Mode", p, Layout.RPMModeHeaderFontSize,
            FontWeight.SemiBold);
        header.Foreground = TrayAppDotNETSettingsUI.Brush(p.SecondaryForeground);
        header.HorizontalAlignment = HorizontalAlignment.Center;
        header.VerticalAlignment = VerticalAlignment.Center;

        Grid grid = NumberRowGrid(Layout.RPMModeHeaderBottomMargin);
        Grid.SetColumn(header, 3);
        grid.Children.Add(header);
        return grid;
    }

    private Grid NumberRow(
        string label,
        SettingsNumberBox value,
        FanPropertyUnitKind unitKind,
        SettingsPalette p,
        double? bottomMargin = null)
    {
        TextBlock labelBlock = RowLabel(label, p);
        SettingsMiniToggle rpmModeToggle = BuildRPMModeToggle(p);
        FanPropertyUnitBinding binding = new(unitKind, value, rpmModeToggle);
        rpmModeToggle.CheckedChanged += (_, isChecked) => OnRPMModeToggleChanged(binding, isChecked);
        _propertyUnitBindings.Add(binding);

        Grid grid = NumberRowGrid(bottomMargin ?? Layout.RowBottomMargin);
        grid.Children.Add(labelBlock);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        Grid.SetColumn(rpmModeToggle, 3);
        grid.Children.Add(rpmModeToggle);
        return grid;
    }

    private Grid NumberRowGrid(double bottomMargin)
    {
        Grid grid = new() { Margin = RowMargin(bottomMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.RowLabelColumnWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.NumberBoxWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.RPMModeToggleGapWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.RPMModeToggleColumnWidth)));
        return grid;
    }

    private SettingsMiniToggle BuildRPMModeToggle(SettingsPalette p)
    {
        SettingsMiniToggle toggle = new(p, BuildRPMModeToggleLayout())
        {
            IsChecked = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        TrayAppDotNETToolTip.SetTip(toggle, "Use RPM units for this property");
        return toggle;
    }

    private SettingsMiniToggleLayout BuildRPMModeToggleLayout() =>
        new()
        {
            Width = Layout.RPMModeToggleTrackWidth,
            TrackWidth = Layout.RPMModeToggleTrackWidth,
            TrackHeight = Layout.RPMModeToggleTrackHeight,
            ThumbSize = Layout.RPMModeToggleThumbSize,
            ThumbHoverSize = Layout.RPMModeToggleThumbHoverSize,
            ThumbCheckedSize = Layout.RPMModeToggleThumbCheckedSize,
            TrackCornerRadius = Layout.RPMModeToggleTrackCornerRadius,
            ThumbCornerRadius = Layout.RPMModeToggleThumbCornerRadius,
            BorderThickness = Layout.RPMModeToggleBorderThickness,
            ThumbUncheckedMargin = Layout.RPMModeToggleThumbUncheckedMargin,
            ThumbCheckedMargin = Layout.RPMModeToggleThumbCheckedMargin,
            EnabledOpacity = Layout.EnabledOpacity,
            DisabledOpacity = Layout.DisabledOpacity
        };

    private void OnRPMModeToggleChanged(FanPropertyUnitBinding binding, bool rpmMode)
    {
        if (_isUpdatingPropertyUnitControls) return;
        UpdatePropertyUnitControl(binding, rpmMode, convertValue: true);
    }

    /// <summary>
    /// Loads property unit controls from persisted fan settings.
    /// </summary>
    private void LoadPropertyUnitControls()
    {
        foreach (FanPropertyUnitBinding binding in _propertyUnitBindings)
            UpdatePropertyUnitControl(binding, FanPropertyRPMMode(binding.Kind), convertValue: false);
    }

    /// <summary>
    /// Reapplies unit ranges after the RPM reference changes.
    /// </summary>
    private void RefreshPropertyUnitControls()
    {
        foreach (FanPropertyUnitBinding binding in _propertyUnitBindings)
            UpdatePropertyUnitControl(binding, binding.RPMMode, convertValue: false);
    }

    private void SetAllPropertyUnitControls(bool rpmMode, bool convertValues)
    {
        foreach (FanPropertyUnitBinding binding in _propertyUnitBindings)
            UpdatePropertyUnitControl(binding, rpmMode, convertValues);
    }

    private void UpdatePropertyUnitControl(FanPropertyUnitBinding binding, bool rpmMode, bool convertValue)
    {
        bool shouldConvertValue = convertValue && binding.RPMMode != rpmMode;
        int value = ReadInt(binding.NumberBox);
        ConfigureNumberBoxUnit(binding, rpmMode);
        SetPropertyUnitToggleState(binding, rpmMode);

        if (shouldConvertValue)
            binding.NumberBox.Value = ConvertSpeedValue(value, binding.RPMMode, rpmMode, RPMReference());

        binding.RPMMode = rpmMode;
    }

    private void ConfigureNumberBoxUnit(FanPropertyUnitBinding binding, bool rpmMode)
    {
        int maximum = rpmMode ? RPMReference() : DutyCycleMaximum;
        int minimum = binding.Kind == FanPropertyUnitKind.Offset ? -maximum : DutyCycleMinimum;
        string suffix = binding.Kind == FanPropertyUnitKind.DeltaMax
            ? rpmMode ? RPMRateSuffix : DutyCycleRateSuffix
            : rpmMode ? RPMSuffix : DutyCycleSuffix;

        binding.NumberBox.Minimum = minimum;
        binding.NumberBox.Maximum = maximum;
        binding.NumberBox.Suffix = suffix;
    }

    private void SetPropertyUnitToggleState(FanPropertyUnitBinding binding, bool rpmMode)
    {
        _isUpdatingPropertyUnitControls = true;
        try
        {
            binding.Toggle.IsChecked = rpmMode;
        }
        finally
        {
            _isUpdatingPropertyUnitControls = false;
        }
    }

    private bool PropertyRPMMode(FanPropertyUnitKind kind)
    {
        foreach (FanPropertyUnitBinding binding in _propertyUnitBindings)
            if (binding.Kind == kind) return binding.Toggle.IsChecked;

        return false;
    }

    private bool FanPropertyRPMMode(FanPropertyUnitKind kind) =>
        kind switch
        {
            FanPropertyUnitKind.StartupSpeed => _fan.StartupSpeedRPMMode,
            FanPropertyUnitKind.ClampHigh => _fan.ClampHighRPMMode,
            FanPropertyUnitKind.ClampLow => _fan.ClampLowRPMMode,
            FanPropertyUnitKind.WarnLow => _fan.WarnLowRPMMode,
            FanPropertyUnitKind.WarnHigh => _fan.WarnHighRPMMode,
            FanPropertyUnitKind.DeltaMax => _fan.DeltaMaxRPMMode,
            FanPropertyUnitKind.Offset => _fan.OffsetRPMMode,
            _ => false
        };

    private int RPMReference()
    {
        Curve? curve = Curve.Find(SelectedTag(_curveCombo));
        int candidate = _fan.MaxRPM > 0
            ? _fan.MaxRPM
            : _fan.CurrentRPM > 0
                ? _fan.CurrentRPM
                : curve?.MaxRPM > 0
                    ? curve.MaxRPM
                    : FallbackMaximumRPM;
        return Math.Max(DutyCycleMaximum, candidate);
    }

    private static int ConvertSpeedValue(int value, bool sourceRPMMode, bool targetRPMMode, int rpmReference)
    {
        if (sourceRPMMode == targetRPMMode) return value;

        double converted = targetRPMMode
            ? value / (double)DutyCycleMaximum * rpmReference
            : value / (double)Math.Max(1, rpmReference) * DutyCycleMaximum;
        return (int)Math.Round(converted);
    }

    private (int low, int high) NormalizeBoundValues(
        int low,
        bool lowRPMMode,
        int high,
        bool highRPMMode)
    {
        int rpmReference = RPMReference();
        int lowDutyCycle = ConvertSpeedValue(low, lowRPMMode, targetRPMMode: false, rpmReference);
        int highDutyCycle = ConvertSpeedValue(high, highRPMMode, targetRPMMode: false, rpmReference);
        if (lowDutyCycle <= highDutyCycle)
            return (low, high);

        return (
            ConvertSpeedValue(high, highRPMMode, lowRPMMode, rpmReference),
            ConvertSpeedValue(low, lowRPMMode, highRPMMode, rpmReference));
    }

    private TextBlock ValueText(SettingsPalette p) =>
        new()
        {
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = Layout.ValueFontSize,
            Foreground = TrayAppDotNETSettingsUI.Brush(p.Foreground),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

    private RadioButton CompactRadio(string text, SettingsPalette p) =>
        new()
        {
            Content = text,
            GroupName = "FanMode",
            Foreground = TrayAppDotNETSettingsUI.Brush(p.Foreground),
            FontSize = Layout.RadioButtonFontSize,
            Margin = Layout.RadioButtonMargin,
            VerticalAlignment = VerticalAlignment.Center
        };

    private Grid Row(string label, Control value, SettingsPalette p, double? bottomMargin = null)
    {
        TextBlock labelBlock = RowLabel(label, p);
        Grid grid = new() { Margin = RowMargin(bottomMargin ?? Layout.RowBottomMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.RowLabelColumnWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Children.Add(labelBlock);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    private TextBlock RowLabel(string label, SettingsPalette p) =>
        new()
        {
            Text = label,
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = Layout.RowLabelFontSize,
            Foreground = TrayAppDotNETSettingsUI.Brush(p.SecondaryForeground),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

    private SettingsButton CaptionButton(Glyph glyph, SettingsPalette p)
    {
        SettingsButton button = new(glyph.Text, p, transparentBase: true)
        {
            Width = Layout.CaptionButtonWidth,
            Height = Layout.CaptionButtonHeight,
            CornerRadius = Layout.ZeroCornerRadius,
            Padding = Layout.CaptionButtonPadding,
            Label = { FontFamily = TrayAppDotNETSettingsUI.IconFont, FontSize = Layout.CaptionButtonGlyphFontSize }
        };
        GlyphApplicator.ApplyTo(button.Label, glyph);
        return button;
    }

    private enum FanPropertyUnitKind
    {
        StartupSpeed,
        ClampHigh,
        ClampLow,
        WarnLow,
        WarnHigh,
        DeltaMax,
        Offset
    }

    private sealed class FanPropertyUnitBinding(
        FanPropertyUnitKind kind,
        SettingsNumberBox numberBox,
        SettingsMiniToggle toggle)
    {
        public FanPropertyUnitKind Kind { get; } = kind;

        public SettingsNumberBox NumberBox { get; } = numberBox;

        public SettingsMiniToggle Toggle { get; } = toggle;

        public bool RPMMode { get; set; }
    }

    private static string GetEffectiveCurveName(Fan fan)
    {
        if (!string.IsNullOrWhiteSpace(fan.Group) && fan.AssignedGroup is { } group)
            return group.AssignedCurveName;

        return fan.AssignedCurveName;
    }

    private static string NormalizeCurveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return string.Equals(name, "None", StringComparison.OrdinalIgnoreCase) ? string.Empty : name;
    }

    private static void SelectComboByTag(SettingsComboBox combo, string? tag)
    {
        string normalized = tag ?? string.Empty;
        foreach (SettingsComboBoxItem item in combo.Items.OfType<SettingsComboBoxItem>())
        {
            if (!string.Equals(item.Tag as string ?? string.Empty, normalized,
                    StringComparison.OrdinalIgnoreCase)) continue;
            combo.SelectedItem = item;
            return;
        }

        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string SelectedTag(SettingsComboBox combo) =>
        combo.SelectedItem is { } item ? item.Tag as string ?? string.Empty : string.Empty;

    private static int ReadInt(SettingsNumberBox box) =>
        box.Value.HasValue ? (int)Math.Round(box.Value.Value) : 0;
}
