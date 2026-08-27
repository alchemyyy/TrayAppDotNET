using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides the shared Task Manager page header and main-content frame.</summary>
internal class TaskManagerPageLayout : Grid
{
    internal TaskManagerPageLayout(
        string title,
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
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
        Grid.SetRow(header, 1);
        Children.Add(header);

        MainContent = new Grid();
        Grid.SetRow(MainContent, 2);
        Children.Add(MainContent);
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
