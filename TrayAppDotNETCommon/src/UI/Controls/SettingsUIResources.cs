using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class SettingsUIResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled settings-UI resource dictionary.
    /// </summary>
    public SettingsUIResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
