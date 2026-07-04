using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI;

public sealed partial class FlyoutUndockButtonResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled flyout-undock-button resource dictionary.
    /// </summary>
    public FlyoutUndockButtonResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
