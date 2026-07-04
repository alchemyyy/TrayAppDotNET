using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class CardsResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled settings-card resource dictionary.
    /// </summary>
    public CardsResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
