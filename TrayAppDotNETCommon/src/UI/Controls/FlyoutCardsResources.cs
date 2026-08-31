using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrayAppDotNETCommon.UI;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class FlyoutCardsResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<FlyoutCardsResources> Resources =
        CommonAXAMLResourceStore<FlyoutCardsResources>.Create(
            "Common flyout-card resources",
            static () => new FlyoutCardsResources(),
            "FlyoutCards.axaml");
#else
    private static readonly Lazy<FlyoutCardsResources> Resources =
        new(static () => new FlyoutCardsResources());
#endif

    /// <summary>
    /// Initializes the compiled flyout-card resource dictionary.
    /// </summary>
    public FlyoutCardsResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static FlyoutCardsResources Current
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
