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
        Children.Add(headerOverlaySpace);

        TextBlock titleText = TrayAppDotNETSettingsUI.Text(
            title,
            palette,
            resources.AxamlTaskManagerPage.TitleFontSize,
            FontWeight.SemiBold);
        titleText.VerticalAlignment = VerticalAlignment.Center;

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
                TaskManagerWindowResources.ProcessGridBackgroundColor),
            CornerRadius = resources.AxamlTaskManagerPage.SurfaceCornerRadius,
            ClipToBounds = true,
            Child = surfaceLayout
        };
        Grid.SetRow(pageSurface, 1);
        Children.Add(pageSurface);
    }

    /// <summary>Gets the right-aligned page-specific header actions.</summary>
    internal StackPanel HeaderActions { get; }

    /// <summary>Gets the full-height content host below the shared header.</summary>
    internal Grid MainContent { get; }

    /// <summary>Gets the control rendered in the shell-level title-bar overlay.</summary>
    internal virtual Control? PageOverlay => null;

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
