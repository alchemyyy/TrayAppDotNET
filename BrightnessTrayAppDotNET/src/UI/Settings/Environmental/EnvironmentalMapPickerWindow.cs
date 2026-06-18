using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Controls.Maps;

namespace BrightnessTrayAppDotNET.UI.Settings.Environmental;

public sealed class EnvironmentalMapPickerWindow : Window
{
    private const double WindowWidth = 760.0;
    private const double WindowHeight = 500.0;
    private const double WindowMinWidth = 560.0;
    private const double WindowMinHeight = 380.0;
    private const double TitleBarHeight = 32.0;
    private const double CloseButtonWidth = 46.0;
    private const double HudButtonSize = 28.0;
    private const double HudClusterSpacing = 8.0;
    private const double HudPadding = 6.0;
    private const string CenterPinGlyph = "\uE73E";

    private readonly SettingsPalette _palette;
    private readonly EnvironmentalMapPickerCanvas _map;
    private readonly TextBlock _coordinateText;

    public EnvironmentalMapPickerWindow(double latitude, double longitude, SettingsPalette palette)
    {
        _palette = palette;
        Title = L("Settings_Environmental_PickOnMap_Title", "Pick location");
        Width = WindowWidth;
        Height = WindowHeight;
        MinWidth = WindowMinWidth;
        MinHeight = WindowMinHeight;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.None;
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);
        Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };

        _map = new EnvironmentalMapPickerCanvas(palette)
        {
            SelectedCoordinate = new GeoCoordinate(latitude, longitude).ClampToWorld(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _map.CoordinateChanged += (_, _) => UpdateCoordinateText();

        _coordinateText = TrayAppDotNETSettingsUI.Text("", palette, 13);
        _coordinateText.FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI");

        Content = BuildContent();
        UpdateCoordinateText();
    }

    public event Action<double, double>? Applied;

    private Border BuildContent()
    {
        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(new GridLength(TitleBarHeight)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Grid titleBar = new() { Background = Brushes.Transparent, Height = TitleBarHeight };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        titleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);
        };

        TextBlock title =
            TrayAppDotNETSettingsUI.Text(L("Settings_Environmental_PickOnMap_Title", "Pick location"), _palette);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(16, 0, 0, 0);
        titleBar.Children.Add(title);

        SettingsButton close = new(GlyphCatalog.CHROME_CLOSE, _palette, transparentBase: true);
        close.Width = CloseButtonWidth;
        close.Height = TitleBarHeight;
        close.Padding = new Thickness(0);
        close.Label.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        close.Click += (_, _) => Close();
        TrayAppDotNETToolTip.SetTip(close, L("Common_Close", "Close"));
        TrayAppDotNETToolTip.SuppressWhileEngaged(close);
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);
        root.Children.Add(titleBar);

        Grid body = new() { Margin = new Thickness(20, 8, 20, 20), };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Border mapHost = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = BuildMapViewport(),
        };
        Grid.SetRow(mapHost, 0);
        body.Children.Add(mapHost);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = new Thickness(1),
            Child = root,
        };
    }

    private Grid BuildMapViewport()
    {
        Grid viewport = new();
        viewport.Children.Add(_map);

        TextBlock crosshair = TrayAppDotNETSettingsUI.Text("+", _palette, 14, FontWeight.SemiBold);
        crosshair.HorizontalAlignment = HorizontalAlignment.Center;
        crosshair.VerticalAlignment = VerticalAlignment.Center;
        crosshair.Opacity = 0.55;
        crosshair.IsHitTestVisible = false;
        viewport.Children.Add(crosshair);

        StackPanel hud = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12),
        };
        hud.Children.Add(BuildCoordinateHud(_palette));
        hud.Children.Add(BuildMapHud(_palette));
        viewport.Children.Add(hud);
        return viewport;
    }

    private Border BuildCoordinateHud(SettingsPalette p)
    {
        SettingsButton apply =
            TrayAppDotNETSettingsCards.Button(L("Settings_MapPicker_Apply_Button", "Apply"), p, new CornerRadius(4));
        SettingsButton abort =
            TrayAppDotNETSettingsCards.Button(L("Settings_MapPicker_Abort_Button", "Abort"), p, new CornerRadius(4));
        apply.MinWidth = 64;
        abort.MinWidth = 64;
        apply.Margin = new Thickness(0, 0, 6, 0);
        apply.Click += (_, _) => ApplyAndClose();
        abort.Click += (_, _) => Close();

        StackPanel buttons = TrayAppDotNETSettingsUI.Horizontal(apply, abort);
        buttons.Margin = new Thickness(0, 8, 0, 0);

        StackPanel panel = new();
        panel.Children.Add(_coordinateText);
        panel.Children.Add(buttons);

        return new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, HudClusterSpacing, 0),
            Background =
                TrayAppDotNETSettingsUI.Brush(Color.FromArgb(232, p.Background.R, p.Background.G, p.Background.B)),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            Child = panel,
        };
    }

    private Border BuildMapHud(SettingsPalette p)
    {
        Grid grid = new();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(6)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        AddHudButton(grid, p, "Up", GlyphCatalog.CHEVRON_UP, 0, 1);
        AddHudButton(grid, p, "Left", GlyphCatalog.CHEVRON_LEFT, 1, 0);
        AddHudButton(grid, p, "Right", GlyphCatalog.CHEVRON_RIGHT, 1, 2);
        AddHudButton(grid, p, "Down", GlyphCatalog.CHEVRON_DOWN, 2, 1);
        AddHudButton(grid, p, "ZoomIn", "+", 0, 4, useIconFont: false);
        AddHudButton(grid, p, "ZoomOut", "-", 1, 4, useIconFont: false);
        AddHudButton(grid, p, "Center", CenterPinGlyph, 2, 4);

        return new Border
        {
            Background =
                TrayAppDotNETSettingsUI.Brush(Color.FromArgb(232, p.Background.R, p.Background.G, p.Background.B)),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(HudPadding),
            Child = grid,
        };
    }

    private void AddHudButton(
        Grid grid,
        SettingsPalette p,
        string action,
        string text,
        int row,
        int column,
        bool useIconFont = true)
    {
        SettingsButton button = TrayAppDotNETSettingsCards.Button(text, p, new CornerRadius(4));
        button.Width = HudButtonSize;
        button.Height = HudButtonSize;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(1);
        if (useIconFont)
            button.Label.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        button.Click += (_, _) => ApplyMapHudAction(action);
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private void ApplyMapHudAction(string action)
    {
        switch (action)
        {
            case "Up":
                _map.PanViewport(0, EnvironmentalMapPickerCanvas.HudPanStep);
                break;
            case "Down":
                _map.PanViewport(0, -EnvironmentalMapPickerCanvas.HudPanStep);
                break;
            case "Left":
                _map.PanViewport(EnvironmentalMapPickerCanvas.HudPanStep, 0);
                break;
            case "Right":
                _map.PanViewport(-EnvironmentalMapPickerCanvas.HudPanStep, 0);
                break;
            case "ZoomIn":
                _map.ZoomAtViewportCenter(EnvironmentalMapPickerCanvas.HudZoomStep);
                break;
            case "ZoomOut":
                _map.ZoomAtViewportCenter(1.0 / EnvironmentalMapPickerCanvas.HudZoomStep);
                break;
            case "Center":
                _map.SetPinToViewportCenter();
                break;
        }
    }

    private void ApplyAndClose()
    {
        GeoCoordinate selected = _map.SelectedCoordinate.ClampToWorld();
        Applied?.Invoke(selected.Latitude, selected.Longitude);
        Close();
    }

    private void UpdateCoordinateText()
    {
        GeoCoordinate selected = _map.SelectedCoordinate;
        _coordinateText.Text = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:F4}, {1:F4}",
            selected.Latitude,
            selected.Longitude);
    }

    private static string L(string key, string fallback)
    {
        try
        {
            string value = LocalizationManager.Instance[key];
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
