using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI;

public sealed partial class CommonBindingsResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled common-binding resource dictionary.
    /// </summary>
    public CommonBindingsResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
