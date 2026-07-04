using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class ColorPickerWindowResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled color-picker resource dictionary.
    /// </summary>
    public ColorPickerWindowResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
