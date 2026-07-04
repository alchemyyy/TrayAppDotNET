using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI;

public sealed partial class SettingsWindowCommonResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled settings-window resource dictionary.
    /// </summary>
    public SettingsWindowCommonResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
