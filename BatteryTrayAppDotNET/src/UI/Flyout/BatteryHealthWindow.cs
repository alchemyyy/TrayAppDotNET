using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BatteryTrayAppDotNET.Services;

namespace BatteryTrayAppDotNET.UI.Flyout;

internal sealed class BatteryHealthWindow : Window, IDisposable
{
    private readonly BatteryMonitorService _batteryMonitor;
    private readonly AppSettings _settings;
    private readonly UIResourceScope _resources = new(nameof(BatteryHealthWindow));
    private TextBlock? _healthValue;
    private TextBlock? _fullChargeValue;
    private TextBlock? _designedValue;
    private SettingsButton? _closeButton;
    private bool _rebuildQueued;
    private bool _closed;

    private static BatteryFlyoutResources.BatteryHealthAxamlProperties Layout =>
        BatteryFlyoutResources.Current.AxamlBatteryHealth;

    public BatteryHealthWindow(BatteryMonitorService batteryMonitor, AppSettings settings)
    {
        _batteryMonitor = batteryMonitor;
        _settings = settings;
        Title = "Battery health";
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        try
        {
            RebuildContent();
            _batteryMonitor.StateChanged += OnBatteryStateChanged;
            _resources.Add(() => _batteryMonitor.StateChanged -= OnBatteryStateChanged);
            _settings.Changed += QueueRebuild;
            _resources.Add(() => _settings.Changed -= QueueRebuild);
#if DEBUG
            BatteryFlyoutResources.ResourcesReloaded += QueueRebuild;
            _resources.Add(() => BatteryFlyoutResources.ResourcesReloaded -= QueueRebuild);
            CommonAXAMLHotReload.ResourcesReloaded += QueueRebuild;
            _resources.Add(() => CommonAXAMLHotReload.ResourcesReloaded -= QueueRebuild);
#endif
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void RebuildContent()
    {
        bool restoreFocus = _closeButton?.IsFocused == true;
        bool isLight = AppTheme.ResolveEffectiveIsLightTheme(_settings);
        SettingsPalette palette = BatterySettingsPalette.Create(AppServices.Theme ?? AppTheme.Default, _settings, isLight);
        StackPanel body = new() { Margin = Layout.BodyMargin, Spacing = Layout.SectionSpacing };
        body.Children.Add(TrayAppDotNETSettingsUI.Text(Title!, palette, Layout.TitleFontSize, FontWeight.SemiBold));

        Grid metrics = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowSpacing = Layout.MetricRowSpacing,
            ColumnSpacing = Layout.MetricValueSpacing
        };
        TextBlock healthValue = AddMetric(metrics, "Health", palette);
        TextBlock fullChargeValue = AddMetric(metrics, "Full charge", palette);
        TextBlock designedValue = AddMetric(metrics, "Designed", palette);
        body.Children.Add(metrics);

        SettingsButton close = TrayAppDotNETSettingsUI.Button(
            LocalizationManager.Instance[nameof(CommonStrings.UpdateDialog_Close)], palette);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        if (!_settings.EnableRoundedCorners) close.CornerRadius = default;
        close.Click += OnCloseClick;
        body.Children.Add(close);

        FlyoutFrame frame = new(body, palette.Background, palette.Border, _settings.EnableRoundedCorners);
        ControlNameScope.For(this).AssignLogicalSubtree(frame, nameof(BatteryHealthWindow));
        Width = Layout.WindowWidth;
        Height = double.NaN;
        Content = frame;
        _healthValue = healthValue;
        _fullChargeValue = fullChargeValue;
        _designedValue = designedValue;
        if (_closeButton != null) _closeButton.Click -= OnCloseClick;
        _closeButton = close;
        RefreshMetrics();
        if (restoreFocus) close.Focus();
    }

    private static TextBlock AddMetric(Grid grid, string label, SettingsPalette palette)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        TextBlock labelText = TrayAppDotNETSettingsUI.Text(label, palette, Layout.MetricFontSize);
        labelText.Foreground = TrayAppDotNETSettingsUI.Brush(palette.SecondaryForeground);
        Grid.SetRow(labelText, row);
        grid.Children.Add(labelText);

        TextBlock valueText = TrayAppDotNETSettingsUI.Text("N/A", palette, Layout.MetricFontSize, FontWeight.SemiBold);
        valueText.TextAlignment = TextAlignment.Right;
        Grid.SetRow(valueText, row);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        return valueText;
    }

    private void OnBatteryStateChanged() => Dispatcher.UIThread.Post(RefreshMetrics, DispatcherPriority.Background);

    private void RefreshMetrics()
    {
        if (_closed) return;
        BatterySnapshot snapshot = _batteryMonitor.Snapshot;
        _healthValue!.Text = snapshot.HealthPercent is { } health ? $"{health:F0}%" : "N/A";
        _fullChargeValue!.Text = FormatCapacity(snapshot.FullChargeCapacityMilliwattHours);
        _designedValue!.Text = FormatCapacity(snapshot.DesignedCapacityMilliwattHours);
    }

    private static string FormatCapacity(float? milliwattHours) =>
        milliwattHours.HasValue ? $"{milliwattHours.Value / 1000f:F1} Wh" : "N/A";

    private void QueueRebuild()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(QueueRebuild, DispatcherPriority.Background);
            return;
        }
        if (_closed || _rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            if (_closed) return;
            try { RebuildContent(); }
            catch (Exception ex) { TADNLog.Log($"BatteryHealthWindow.Rebuild: {ex.Message}"); }
        }, DispatcherPriority.Background);
    }

    private void OnCloseClick(object? sender, EventArgs e) => Close();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _closeButton?.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        try { Dispose(); }
        finally { base.OnClosed(e); }
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        try
        {
            if (IsVisible) Close();
        }
        finally
        {
            _resources.Dispose();
            if (_closeButton != null) _closeButton.Click -= OnCloseClick;
            _closeButton = null;
            Content = null;
        }
    }
}
