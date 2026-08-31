using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace TrayAppDotNETCommon.Visuals;

public sealed partial class AppThemeResources : ResourceDictionary
{
    /// <summary>Initializes the compiled common theme-color dictionary.</summary>
    public AppThemeResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Reads a common theme color from this dictionary.</summary>
    public ThemeColor Color(string name) => AppThemeResourceReader.Color(this, prefix: "AppTheme", name);

    /// <summary>Reads a single common color from this dictionary.</summary>
    public Color SingleColor(string name) => AppThemeResourceReader.SingleColor(this, prefix: "AppTheme", name);
}
