using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class FlyoutFrameResources : ResourceDictionary
{
    /// <summary>Initializes the compiled flyout-frame resource dictionary.</summary>
    public FlyoutFrameResources() => AvaloniaXamlLoader.Load(this);
}
