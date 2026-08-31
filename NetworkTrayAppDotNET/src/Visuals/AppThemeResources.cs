using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrayAppDotNETCommon.Visuals;

namespace NetworkTrayAppDotNET.Visuals;

public sealed class AppThemeResources : ResourceDictionary
{
    /// <summary>Initializes the compiled network theme-color dictionary.</summary>
    public AppThemeResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Reads a network theme color from this dictionary.</summary>
    public ThemeColor Color(string name) =>
        AppThemeResourceReader.Color(this, prefix: "NetworkAppTheme", name);
}

internal static class AppThemeColorCatalog
{
#if DEBUG
    private static readonly AppThemeHotReloadStore<AppThemeResources> Resources =
        AppThemeHotReloadStore<AppThemeResources>.Create(
            catalogName: "Network",
            static () => new AppThemeResources());
#else
    private static readonly Lazy<AppThemeResources> Resources = new(static () => new AppThemeResources());
#endif

    /// <summary>Gets a network theme color from the active resource dictionary.</summary>
    public static ThemeColor Color(string name)
    {
#if DEBUG
        return Resources.Current.Color(name);
#else
        return Resources.Value.Color(name);
#endif
    }
}
