using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;

namespace BrightnessTrayAppDotNET.Visuals;

public sealed partial class AppThemeResources : ResourceDictionary
{
    /// <summary>Initializes the compiled brightness theme-color dictionary.</summary>
    public AppThemeResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Reads a brightness theme color from this dictionary.</summary>
    public ThemeColor Color(string name) =>
        AppThemeResourceReader.Color(this, prefix: "BrightnessAppTheme", name);

    /// <summary>Reads a single brightness color from this dictionary.</summary>
    public Color SingleColor(string name) =>
        AppThemeResourceReader.SingleColor(this, prefix: "BrightnessAppTheme", name);
}

internal static class AppThemeColorCatalog
{
#if DEBUG
    private static readonly AppThemeHotReloadStore<AppThemeResources> Resources =
        AppThemeHotReloadStore<AppThemeResources>.Create(
            catalogName: "Brightness",
            static () => new AppThemeResources());
#else
    private static readonly Lazy<AppThemeResources> Resources = new(static () => new AppThemeResources());
#endif

    /// <summary>Gets a brightness theme color from the active resource dictionary.</summary>
    public static ThemeColor Color(string name)
    {
#if DEBUG
        return Resources.Current.Color(name);
#else
        return Resources.Value.Color(name);
#endif
    }

    /// <summary>Gets a single brightness color from the active resource dictionary.</summary>
    public static Color SingleColor(string name)
    {
#if DEBUG
        return Resources.Current.SingleColor(name);
#else
        return Resources.Value.SingleColor(name);
#endif
    }
}
