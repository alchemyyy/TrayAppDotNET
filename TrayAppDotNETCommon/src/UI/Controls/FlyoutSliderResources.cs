using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class FlyoutSliderResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<FlyoutSliderResources> Resources =
        CommonAXAMLResourceStore<FlyoutSliderResources>.Create(
            resourceName: "Common flyout-slider resources",
            static () => new FlyoutSliderResources(),
            sourceFileName: "FlyoutSlider.axaml");
#else
    private static readonly Lazy<FlyoutSliderResources> Resources =
        new(static () => new FlyoutSliderResources());
#endif

    /// <summary>
    /// Initializes the compiled flyout-slider resource dictionary.
    /// </summary>
    public FlyoutSliderResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static FlyoutSliderResources Current
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
