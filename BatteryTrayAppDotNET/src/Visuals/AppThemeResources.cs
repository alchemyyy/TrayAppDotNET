using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrayAppDotNETCommon.Visuals;

namespace BatteryTrayAppDotNET.Visuals;

public sealed partial class AppThemeResources : ResourceDictionary
{
    /// <summary>Initializes the compiled battery theme-color dictionary.</summary>
    public AppThemeResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Reads a battery theme color from this dictionary.</summary>
    public ThemeColor Color(string name) =>
        AppThemeResourceReader.Color(this, "BatteryAppTheme", name);
}

internal static class AppThemeColorCatalog
{
#if DEBUG
    private static readonly AppThemeHotReloadStore<AppThemeResources> Resources =
        AppThemeHotReloadStore<AppThemeResources>.Create(
            "Battery",
            static () => new AppThemeResources());
#else
    private static readonly Lazy<AppThemeResources> Resources = new(static () => new AppThemeResources());
#endif

    /// <summary>Gets a battery theme color from the active resource dictionary.</summary>
    public static ThemeColor Color(string name)
    {
#if DEBUG
        return Resources.Current.Color(name);
#else
        return Resources.Value.Color(name);
#endif
    }
}
