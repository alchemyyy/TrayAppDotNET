using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BatteryTrayAppDotNET.Models;
using BatteryTrayAppDotNET.Services;

namespace BatteryTrayAppDotNET.UI.Flyout;

public sealed class BatteryFlyoutWindow : FlyoutWindowCommon
{
    private const int EdgePadding = 8;
    private const int OffscreenPosition = -32000;

    private readonly BatteryMonitorService _batteryMonitor;
    private readonly AppSettings _settings;
    private TrayAppDotNETShellTrayIcon? _lastTrayIcon;

    public BatteryFlyoutWindow(BatteryMonitorService batteryMonitor, AppSettings settings)
    {
        _batteryMonitor = batteryMonitor;
        _settings = settings;

        Width = 350;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;

        _batteryMonitor.StateChanged += OnBatteryStateChanged;
        Closed += OnClosed;
        Rebuild();
    }

    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, bool activate = true)
    {
        _lastTrayIcon = trayIcon;
        ShowActivated = activate;
        MaxHeight = ResolveWorkArea(trayIcon).Height / RenderScaling - (2 * EdgePadding);
        Rebuild();

        if (!IsVisible)
        {
            Opacity = 1;
            Position = new PixelPoint(OffscreenPosition, OffscreenPosition);
            Show();
        }

        Dispatcher.UIThread.Post(() =>
        {
            UpdateLayout();
            PositionNearTray();
            if (activate) Activate();
        }, DispatcherPriority.Loaded);
    }

    public new void Hide()
    {
        base.Hide();
        NotifyWarmDismissed();
    }

    protected override void HideFlyout() => Hide();

    private void OnBatteryStateChanged() => Dispatcher.UIThread.Post(Rebuild);

    private void Rebuild()
    {
        bool isLight = AppTheme.ResolveEffectiveIsLightTheme(_settings);
        SettingsPalette p = BatterySettingsPalette.Create(AppServices.Theme, _settings, isLight);
        BatterySnapshot snapshot = _batteryMonitor.Snapshot;

        StackPanel root = new()
        {
            Margin = new Thickness(16),
            Spacing = 10,
        };

        TextBlock title = Text("Battery", p, 20, FontWeight.SemiBold);
        root.Children.Add(title);

        TextBlock percent = Text(snapshot.BatteryPresent ? $"{snapshot.ChargePercentage}%" : "--", p, 48, FontWeight.Bold);
        percent.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(percent);

        root.Children.Add(BatteryBar(snapshot, p));

        TextBlock status = Text(BuildStatus(snapshot), p, 14);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        status.Foreground = Brush(p.SecondaryForeground);
        root.Children.Add(status);

        root.Children.Add(Separator(p));

        if (snapshot.EstimatedTimeRemaining.HasValue && !snapshot.IsFullyCharged)
        {
            root.Children.Add(DetailBlock(
                snapshot.IsCharging ? "Time until full" : "Battery life",
                FormatTimeSpan(snapshot.EstimatedTimeRemaining.Value),
                p));
            root.Children.Add(Separator(p));
        }

        Grid details = new()
        {
            RowSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };

        AddDetailRow(details, 0, "Power source", snapshot.IsOnExternalPower ? "External power" : "Battery", p);
        AddDetailRow(details, 1, "Battery power", FormatPower(snapshot.CurrentBatteryPowerWatts), p);
        AddDetailRow(details, 2, "Remaining", FormatCapacity(snapshot.RemainingCapacityMilliwattHours), p);
        AddDetailRow(details, 3, "Full charge", FormatCapacity(snapshot.FullChargeCapacityMilliwattHours), p);
        AddDetailRow(details, 4, "Designed", FormatCapacity(snapshot.DesignedCapacityMilliwattHours), p);
        AddDetailRow(details, 5, "Health", snapshot.HealthPercent.HasValue ? $"{snapshot.HealthPercent.Value:F0}%" : "N/A", p);
        root.Children.Add(details);

        Content = new Border
        {
            Background = Brush(p.Background),
            BorderBrush = Brush(p.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(_settings.EnableRoundedCorners ? 8 : 0),
            Child = root,
        };
    }

    private void PositionNearTray()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        int width = Math.Max(1, (int)Math.Ceiling(Math.Max(Bounds.Width, Width) * RenderScaling));
        int height = Math.Max(1, (int)Math.Ceiling(Math.Max(Bounds.Height, MinHeight) * RenderScaling));
        int left = _lastTrayIcon?.TryGetIconRect(out PixelRect rect) == true
            ? rect.Center.X - width / 2
            : workArea.Right - width - EdgePadding;
        int top = workArea.Bottom - height - EdgePadding;
        Position = new PixelPoint(
            Math.Clamp(left, workArea.X + EdgePadding, Math.Max(workArea.X + EdgePadding, workArea.Right - width - EdgePadding)),
            Math.Clamp(top, workArea.Y + EdgePadding, Math.Max(workArea.Y + EdgePadding, workArea.Bottom - height - EdgePadding)));
    }

    private PixelRect ResolveWorkArea(TrayAppDotNETShellTrayIcon? trayIcon)
    {
        PixelPoint anchor = Position;
        if (trayIcon?.TryGetIconRect(out PixelRect iconRect) == true)
            anchor = iconRect.Center;

        return (Screens.ScreenFromPoint(anchor) ?? Screens.Primary)?.WorkingArea
            ?? new PixelRect(0, 0, 1920, 1080);
    }

    private static Border BatteryBar(BatterySnapshot snapshot, SettingsPalette p)
    {
        Grid bar = new()
        {
            Width = 230,
            Height = 18,
            ClipToBounds = true,
        };

        bar.Children.Add(new Border
        {
            Background = Brush(p.ControlBackground),
            BorderBrush = Brush(p.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        });

        double fillWidth = Math.Max(0, 228 * snapshot.ChargePercentage / 100.0);
        bar.Children.Add(new Border
        {
            Width = fillWidth,
            Height = 16,
            Margin = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush(ResolveFillColor(snapshot, p)),
            CornerRadius = new CornerRadius(3),
        });

        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = bar,
        };
    }

    private static Color ResolveFillColor(BatterySnapshot snapshot, SettingsPalette p)
    {
        if (snapshot.IsCharging) return Color.FromRgb(0x10, 0x7C, 0x10);
        if (!snapshot.IsOnExternalPower && snapshot.ChargePercentage <= 10) return Color.FromRgb(0xE8, 0x11, 0x23);
        if (!snapshot.IsOnExternalPower && snapshot.ChargePercentage <= 20) return Color.FromRgb(0xFF, 0xB9, 0x00);
        return p.Accent;
    }

    private static StackPanel DetailBlock(string label, string value, SettingsPalette p)
    {
        StackPanel panel = new() { Spacing = 3 };
        TextBlock labelText = Text(label, p, 13);
        labelText.Foreground = Brush(p.SecondaryForeground);
        panel.Children.Add(labelText);
        panel.Children.Add(Text(value, p, 18, FontWeight.SemiBold));
        return panel;
    }

    private static void AddDetailRow(Grid grid, int row, string label, string value, SettingsPalette p)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        TextBlock labelText = Text(label + ":", p, 12);
        labelText.Foreground = Brush(p.SecondaryForeground);
        labelText.Margin = new Thickness(0, 0, 14, 0);
        Grid.SetRow(labelText, row);
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        TextBlock valueText = Text(value, p, 12);
        valueText.TextAlignment = TextAlignment.Right;
        Grid.SetRow(valueText, row);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
    }

    private static Border Separator(SettingsPalette p) =>
        new()
        {
            Height = 1,
            Background = Brush(p.Border),
            Opacity = 0.75,
        };

    private static TextBlock Text(string text, SettingsPalette p, double size, FontWeight weight = FontWeight.Normal) =>
        new()
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = size,
            FontWeight = weight,
            Foreground = Brush(p.Foreground),
        };

    private static SolidColorBrush Brush(Color color) => new(color);

    private static string BuildStatus(BatterySnapshot snapshot)
    {
        if (!snapshot.BatteryPresent) return "No battery detected";
        if (snapshot.IsFullyCharged) return "Plugged in, full";
        if (snapshot.IsCharging)
        {
            return snapshot.ChargeRateWatts.HasValue
                ? $"Charging at {snapshot.ChargeRateWatts.Value:F1} W"
                : "Charging";
        }

        if (snapshot.IsOnExternalPower) return "Plugged in";
        return "On battery";
    }

    private static string FormatPower(float? watts) =>
        watts.HasValue ? $"{watts.Value:F1} W" : "N/A";

    private static string FormatCapacity(float? milliwattHours) =>
        milliwattHours.HasValue ? $"{milliwattHours.Value / 1000f:F1} Wh" : "N/A";

    private static string FormatTimeSpan(TimeSpan value)
    {
        if (value.TotalDays >= 1) return $"{(int)value.TotalDays}d {value.Hours}h";
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours}h {value.Minutes}m";
        return $"{Math.Max(1, value.Minutes)}m";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _batteryMonitor.StateChanged -= OnBatteryStateChanged;
        Closed -= OnClosed;
    }
}
