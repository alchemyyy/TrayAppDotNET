using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Exposes search-overlay geometry used by restored-window drag avoidance.</summary>
internal interface ITaskManagerSearchOverlayPage
{
    bool TryGetSearchDragRegionPixelWidths(out int searchWidth, out int leadingActionWidth);
}

/// <summary>Provides the shared Task Manager page header and main-content frame.</summary>
internal class TaskManagerPageLayout : Grid
{
#if DEBUG
    private readonly Grid _headerOverlaySpace;
    private readonly TextBlock _titleText;
    private readonly Grid _header;
    private readonly Border _headerFrame;
    private readonly Border _pageSurface;
#endif

    internal TaskManagerPageLayout(
        string title,
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Grid headerOverlaySpace = new()
        {
            Height = resources.AxamlTaskManagerPage.HeaderOverlayHeight,
            Margin = resources.AxamlTaskManagerPage.HeaderOverlayMargin
        };
#if DEBUG
        _headerOverlaySpace = headerOverlaySpace;
#endif
        Children.Add(headerOverlaySpace);

        TextBlock titleText = TrayAppDotNETSettingsUI.Text(
            title,
            palette,
            resources.AxamlTaskManagerPage.TitleFontSize,
            (FontWeight)resources.AxamlTaskManagerPage.TitleFontWeight);
        titleText.VerticalAlignment = VerticalAlignment.Center;
#if DEBUG
        _titleText = titleText;
#endif

        HeaderActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = resources.AxamlTaskManagerPage.HeaderSpacing,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = new()
        {
            Height = resources.AxamlTaskManagerPage.HeaderContentHeight,
            Margin = resources.AxamlTaskManagerPage.HeaderMargin,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
#if DEBUG
        _header = header;
#endif
        header.Children.Add(titleText);
        Grid.SetColumn(HeaderActions, 1);
        header.Children.Add(HeaderActions);

        MainContent = new Grid();
        Border headerFrame = new()
        {
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = new Thickness(
                0,
                0,
                0,
                resources.AxamlProcessTable.GridLineThickness),
            Child = header
        };
#if DEBUG
        _headerFrame = headerFrame;
#endif
        Grid surfaceLayout = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Children = { headerFrame, MainContent }
        };
        Grid.SetRow(MainContent, 1);

        Border pageSurface = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(
                resources.AxamlProcessTable.GridBackgroundColor),
            CornerRadius = resources.AxamlTaskManagerPage.SurfaceCornerRadius,
            ClipToBounds = true,
            Child = surfaceLayout
        };
#if DEBUG
        _pageSurface = pageSurface;
#endif
        Grid.SetRow(pageSurface, 1);
        Children.Add(pageSurface);
    }

    /// <summary>Gets the right-aligned page-specific header actions.</summary>
    internal StackPanel HeaderActions { get; }

    /// <summary>Gets the full-height content host below the shared header.</summary>
    internal Grid MainContent { get; }

    /// <summary>Gets the control rendered in the shell-level title-bar overlay.</summary>
    internal virtual Control? PageOverlay => null;

#if DEBUG
    /// <summary>Applies shared Task Manager page resources without replacing page-owned runtime state.</summary>
    internal virtual void ApplyAXAMLResources(TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        _headerOverlaySpace.Height = resources.AxamlTaskManagerPage.HeaderOverlayHeight;
        _headerOverlaySpace.Margin = resources.AxamlTaskManagerPage.HeaderOverlayMargin;
        _titleText.FontSize = resources.AxamlTaskManagerPage.TitleFontSize;
        _titleText.FontWeight = (FontWeight)resources.AxamlTaskManagerPage.TitleFontWeight;
        HeaderActions.Spacing = resources.AxamlTaskManagerPage.HeaderSpacing;
        _header.Height = resources.AxamlTaskManagerPage.HeaderContentHeight;
        _header.Margin = resources.AxamlTaskManagerPage.HeaderMargin;
        _headerFrame.BorderThickness = new Thickness(
            0,
            0,
            0,
            resources.AxamlProcessTable.GridLineThickness);
        _pageSurface.Background = TrayAppDotNETSettingsUI.Brush(
            resources.AxamlProcessTable.GridBackgroundColor);
        _pageSurface.CornerRadius = resources.AxamlTaskManagerPage.SurfaceCornerRadius;
    }
#endif

    /// <summary>Starts or stops page-specific work as navigation and window visibility change.</summary>
    internal virtual void SetPageActive(bool isActive)
    {
    }

    /// <summary>Gets the main-content top edge in another control's coordinate space.</summary>
    internal bool TryGetMainContentTop(Control relativeTo, out double contentTop)
    {
        Point? contentOrigin = MainContent.TranslatePoint(default, relativeTo);
        if (!contentOrigin.HasValue)
        {
            contentTop = 0;
            return false;
        }

        contentTop = contentOrigin.Value.Y;
        return true;
    }
}
