using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed partial class TrayAppDotNETAboutPageResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<TrayAppDotNETAboutPageResources> Resources =
        CommonAXAMLResourceStore<TrayAppDotNETAboutPageResources>.Create(
            resourceName: "Common about-page resources",
            static () => new TrayAppDotNETAboutPageResources(),
            sourceFileName: "TrayAppDotNETAboutPage.axaml");
#else
    private static readonly Lazy<TrayAppDotNETAboutPageResources> Resources =
        new(static () => new TrayAppDotNETAboutPageResources());
#endif

    /// <summary>
    /// Initializes the compiled about-page resource dictionary.
    /// </summary>
    public TrayAppDotNETAboutPageResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static TrayAppDotNETAboutPageResources Current
    {
        get
        {
#if DEBUG
            return Resources.Current;
#else
            return Resources.Value;
#endif
        }
    }
}
