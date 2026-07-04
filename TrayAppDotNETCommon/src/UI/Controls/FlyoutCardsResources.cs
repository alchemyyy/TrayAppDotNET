using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class FlyoutCardsResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled flyout-card resource dictionary.
    /// </summary>
    public FlyoutCardsResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
