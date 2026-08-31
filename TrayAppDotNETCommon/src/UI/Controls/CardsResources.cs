using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrayAppDotNETCommon.UI;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class CardsResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<CardsResources> Resources =
        CommonAXAMLResourceStore<CardsResources>.Create(
            "Common settings-card resources",
            static () => new CardsResources(),
            "Cards.axaml");
#else
    private static readonly Lazy<CardsResources> Resources =
        new(static () => new CardsResources());
#endif

    /// <summary>
    /// Initializes the compiled settings-card resource dictionary.
    /// </summary>
    public CardsResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static CardsResources Current
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
