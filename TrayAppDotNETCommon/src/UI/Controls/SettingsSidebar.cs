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
        Footer = new StackPanel();
        FooterHost = new StackPanel { Margin = footerMargin, Children = { Footer } };

        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        SetRow(Navigation, value: 1);
        Children.Add(Navigation);
        SetRow(FooterHost, value: 2);
        Children.Add(FooterHost);
    }

    public Thickness HeaderMargin { get; }
    public StackPanel Navigation { get; }

    /// <summary>Gets the footer navigation rows, a focus group of their own.</summary>
    public StackPanel Footer { get; }

    /// <summary>
    /// Gets the footer panel that holds <see cref="Footer"/> and the controls below it, such as the settings search
    /// box, which must stay outside the rows' focus group.
    /// </summary>
    public StackPanel FooterHost { get; }

    /// <summary>Adds the application header to the sidebar's first row.</summary>
    public void SetHeader(Control header)
    {
        ArgumentNullException.ThrowIfNull(header);
        header.Margin = HeaderMargin;
        SetRow(header, value: 0);
        Children.Add(header);
    }
}
