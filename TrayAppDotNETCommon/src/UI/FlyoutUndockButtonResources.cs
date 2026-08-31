using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI;

public sealed partial class FlyoutUndockButtonResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<FlyoutUndockButtonResources> Resources =
        CommonAXAMLResourceStore<FlyoutUndockButtonResources>.Create(
            "Common flyout-undock-button resources",
            static () => new FlyoutUndockButtonResources(),
            "FlyoutUndockButtonController.axaml");
#else
    private static readonly Lazy<FlyoutUndockButtonResources> Resources =
        new(static () => new FlyoutUndockButtonResources());
#endif

    /// <summary>
    /// Initializes the compiled flyout-undock-button resource dictionary.
    /// </summary>
    public FlyoutUndockButtonResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static FlyoutUndockButtonResources Current
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
