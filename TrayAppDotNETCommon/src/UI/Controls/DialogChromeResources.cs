using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class DialogChromeResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled dialog-chrome resource dictionary.
    /// </summary>
    public DialogChromeResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
