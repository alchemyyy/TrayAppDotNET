using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class FlyoutSliderResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled flyout-slider resource dictionary.
    /// </summary>
    public FlyoutSliderResources() => AvaloniaXamlLoader.Load(this);
}
