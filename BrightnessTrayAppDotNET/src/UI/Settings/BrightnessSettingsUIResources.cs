using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsUIResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled brightness settings-UI resource dictionary.
    /// </summary>
    public BrightnessSettingsUIResources() => AvaloniaXamlLoader.Load(this);
}
