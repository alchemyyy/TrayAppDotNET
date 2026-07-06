using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed partial class TrayAppDotNETAboutPageResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled about-page resource dictionary.
    /// </summary>
    public TrayAppDotNETAboutPageResources() => AvaloniaXamlLoader.Load(this);
}
