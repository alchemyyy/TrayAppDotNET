using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;

namespace VolumeTrayAppDotNET.Visuals;

public sealed partial class AppThemeResources : ResourceDictionary
{
    /// <summary>Initializes the compiled volume theme-color dictionary.</summary>
    public AppThemeResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Reads a volume theme color from this dictionary.</summary>
    public Color Color(string name) => AppThemeResourceReader.SingleColor(this, prefix: "VolumeAppTheme", name);
}

internal static class AppThemeColorCatalog
{
#if DEBUG
    private static readonly AppThemeHotReloadStore<AppThemeResources> Resources =
        AppThemeHotReloadStore<AppThemeResources>.Create(
            catalogName: "Volume",
            static () => new AppThemeResources());
#else
    private static readonly Lazy<AppThemeResources> Resources = new(static () => new AppThemeResources());
#endif

    /// <summary>Gets a volume theme color from the active resource dictionary.</summary>
    public static Color Color(string name)
    {
#if DEBUG
        return Resources.Current.Color(name);
#else
        return Resources.Value.Color(name);
#endif
    }
}
