using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Controls.Maps;
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;

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

    private readonly SettingsPalette _palette;
    private readonly AppTheme _theme;
    private readonly AppSettings _settings;
    private readonly bool _isLight;
    private readonly ControlNameScope _controlNames;
    private readonly EnvironmentalMapPickerCanvas _map;
    private readonly TextBlock _coordinateText;
    private readonly List<(TextBlock Target, Func<Glyph> Resolve)> _glyphBindings = [];
    private Border _mapHud = null!;
    private bool _isRetiring;

    public EnvironmentalMapPickerWindow(
        double latitude,
        double longitude,
        SettingsPalette palette,
        AppTheme theme,
        AppSettings settings,
        bool isLight)
    {
        _controlNames = ControlNameScope.For(this);
        _palette = palette;
        _theme = theme;
        _settings = settings;
        _isLight = isLight;
        Title = L(nameof(AppStrings.Settings_Environmental_PickOnMap_Title));
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
        Closing += OnClosing;
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Hide();
            e.Handled = true;
        };

        _map = _controlNames.Assign(
            new EnvironmentalMapPickerCanvas(palette, theme.EnvironmentalMapPin.For(isLight))
            {
                SelectedCoordinate = new GeoCoordinate(latitude, longitude).ClampToWorld(),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            },
            "MapViewport");
        _map.CoordinateChanged += (_, _) => UpdateCoordinateText();

        _coordinateText = _controlNames.Assign(
            TrayAppDotNETSettingsUI.Text("", palette, 13),
            "CoordinateHUD");
        _coordinateText.FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI");

        Border content = BuildContent();
        _controlNames.AssignLogicalSubtree(content, nameof(EnvironmentalMapPickerWindow));
        Content = content;
        _settings.Changed += OnSettingsChanged;
        GlyphCatalogHotReload.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
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
            TrayAppDotNETSettingsUI.Text(L(nameof(AppStrings.Settings_Environmental_PickOnMap_Title)), _palette);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(16, 0, 0, 0);
        titleBar.Children.Add(title);

        SettingsButton close = _controlNames.Assign(
            new SettingsButton(GlyphCatalog.CHROME_CLOSE.Text, _palette, transparentBase: true)
            {
                Width = CloseButtonWidth,
                Height = TitleBarHeight,
                Padding = new Thickness(0),
                Label = { FontFamily = TrayAppDotNETSettingsUI.IconFont }
            },
            "TitleBar");
        BindGlyph(close, static () => GlyphCatalog.CHROME_CLOSE);
        close.Click += (_, _) => Hide();
        TrayAppDotNETToolTip.SetTip(close, L(nameof(CommonStrings.Common_Close)));
        TrayAppDotNETToolTip.SuppressWhileEngaged(close);
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);
        root.Children.Add(titleBar);

        Grid body = new() { Margin = new Thickness(20, 8, 20, 20) };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Border mapHost = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = BuildMapViewport()
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
            Child = root
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
            Margin = new Thickness(12)
        };
        hud.Children.Add(BuildCoordinateHud(_palette));
        _mapHud = BuildMapHud(_palette);
        hud.Children.Add(_mapHud);
        viewport.Children.Add(hud);
        return viewport;
    }

    private Border BuildCoordinateHud(SettingsPalette p)
    {
        SettingsButton apply = _controlNames.Assign(
            TrayAppDotNETSettingsCards.Button(
                L(nameof(AppStrings.Settings_MapPicker_Apply_Button)),
                p,
                new CornerRadius(4)),
            "CoordinateHUD");
        SettingsButton abort = _controlNames.Assign(
            TrayAppDotNETSettingsCards.Button(
                L(nameof(AppStrings.Settings_MapPicker_Abort_Button)),
                p,
                new CornerRadius(4)),
            "CoordinateHUD");
        apply.MinWidth = 64;
        abort.MinWidth = 64;
        apply.Margin = new Thickness(0, 0, 6, 0);
        apply.Click += (_, _) => ApplyAndClose();
        abort.Click += (_, _) => Hide();

        StackPanel buttons = TrayAppDotNETSettingsUI.Horizontal(apply, abort);
        buttons.Margin = new Thickness(0, 8, 0, 0);

        StackPanel panel = new();
        panel.Children.Add(_coordinateText);
        panel.Children.Add(buttons);

        return new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, HudClusterSpacing, 0),
            Background = TrayAppDotNETSettingsUI.Brush(_theme.ResolveEnvironmentalMapHudBackdrop(_settings, _isLight)),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            Child = panel
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

        AddHudGlyphButton(grid, p, "Up", static () => GlyphCatalog.CHEVRON_UP, 0, 1);
        AddHudGlyphButton(grid, p, "Left", static () => GlyphCatalog.CHEVRON_LEFT, 1, 0);
        AddHudGlyphButton(grid, p, "Right", static () => GlyphCatalog.CHEVRON_RIGHT, 1, 2);
        AddHudGlyphButton(grid, p, "Down", static () => GlyphCatalog.CHEVRON_DOWN, 2, 1);
        AddHudButton(grid, p, "ZoomIn", "+", 0, 4, useIconFont: false);
        AddHudButton(grid, p, "ZoomOut", "-", 1, 4, useIconFont: false);
        AddHudGlyphButton(grid, p, "Center", static () => GlyphCatalog.MAP_CENTER, 2, 4);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(_theme.ResolveEnvironmentalMapHudBackdrop(_settings, _isLight)),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(HudPadding),
            Child = grid
        };
    }

    private void AddHudGlyphButton(
        Grid grid,
        SettingsPalette palette,
        string action,
        Func<Glyph> resolveGlyph,
        int row,
        int column)
    {
        Glyph glyph = resolveGlyph();
        SettingsButton button = AddHudButton(grid, palette, action, glyph.Text, row, column);
        BindGlyph(button, resolveGlyph);
    }

    private SettingsButton AddHudButton(
        Grid grid,
        SettingsPalette p,
        string action,
        string text,
        int row,
        int column,
        bool useIconFont = true)
    {
        SettingsButton button = _controlNames.Assign(
            TrayAppDotNETSettingsCards.Button(text, p, new CornerRadius(4)),
            $"MapHUD{action}");
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
        return button;
    }

    /// <summary>
    /// Associates a persistent button label with its live glyph definition.
    /// </summary>
    private void BindGlyph(SettingsButton button, Func<Glyph> resolveGlyph)
    {
        Glyph glyph = resolveGlyph();
        GlyphApplicator.ApplyTo(button.Label, glyph);
        _glyphBindings.Add((button.Label, resolveGlyph));
    }

    /// <summary>
    /// Reapplies glyph metadata to controls that remain alive while the picker is hidden.
    /// </summary>
    private void OnGlyphCatalogResourcesReloaded()
    {
        if (_isRetiring) return;

        foreach ((TextBlock target, Func<Glyph> resolve) in _glyphBindings)
        {
            target.RenderTransform = null;
            target.FontWeight = FontWeight.Normal;
            GlyphApplicator.ApplyTo(target, resolve());
        }
    }

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_isRetiring) return;

        bool isLight = AppTheme.ResolveEffectiveIsLightTheme(_settings);
        _palette.UpdateFrom(BrightnessSettingsWindow.CreatePalette(AppServices.Theme, _settings, isLight));
        _map.SetPinColor(_theme.EnvironmentalMapPin.For(isLight));
        _mapHud.Background = TrayAppDotNETSettingsUI.Brush(
            _theme.ResolveEnvironmentalMapHudBackdrop(_settings, isLight));
    });

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
        Hide();
    }

    /// <summary>Closes the page-owned picker when its settings-page generation retires.</summary>
    internal void CloseForPageRetirement()
    {
        _isRetiring = true;
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!ShouldCancelCloseForReuse(_isRetiring, e.CloseReason))
        {
            _isRetiring = true;
            return;
        }

        e.Cancel = true;
        Hide();
    }

    internal static bool ShouldCancelCloseForReuse(bool isRetiring, WindowCloseReason closeReason) =>
        !isRetiring
        && closeReason is not (WindowCloseReason.OwnerWindowClosing
            or WindowCloseReason.ApplicationShutdown
            or WindowCloseReason.OSShutdown);

    protected override void OnClosed(EventArgs e)
    {
        Closing -= OnClosing;
        _settings.Changed -= OnSettingsChanged;
        GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded;
        _glyphBindings.Clear();
        Applied = null;
        try
        {
            _map.Dispose();
        }
        finally
        {
            try { Content = null; }
            finally { base.OnClosed(e); }
        }
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

    private static string L(string key) => LocalizationManager.Instance[key];
}
