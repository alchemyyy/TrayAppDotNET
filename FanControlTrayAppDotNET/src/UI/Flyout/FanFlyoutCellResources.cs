using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FanControlTrayAppDotNET.UI;

public sealed partial class FanFlyoutCellResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled fan-flyout-cell resource dictionary.
    /// </summary>
    public FanFlyoutCellResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
