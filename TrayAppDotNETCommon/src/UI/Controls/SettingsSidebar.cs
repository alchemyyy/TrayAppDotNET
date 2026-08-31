using Avalonia;
using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>Hosts the header and navigation groups for a settings-style sidebar.</summary>
public class SettingsSidebar : Grid
{
    public SettingsSidebar(
        Thickness headerMargin,
        Thickness navigationMargin,
        Thickness footerMargin)
    {
        HeaderMargin = headerMargin;
        Navigation = new StackPanel { Margin = navigationMargin };
        Footer = new StackPanel { Margin = footerMargin };

        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid.SetRow(Navigation, 1);
        Children.Add(Navigation);
        Grid.SetRow(Footer, 2);
        Children.Add(Footer);
    }

    public Thickness HeaderMargin { get; }
    public StackPanel Navigation { get; }
    public StackPanel Footer { get; }

    /// <summary>Adds the application header to the sidebar's first row.</summary>
    public void SetHeader(Control header)
    {
        ArgumentNullException.ThrowIfNull(header);
        header.Margin = HeaderMargin;
        Grid.SetRow(header, 0);
        Children.Add(header);
    }
}
