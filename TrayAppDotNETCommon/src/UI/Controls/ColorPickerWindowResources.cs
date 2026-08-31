using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class ColorPickerWindowResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<ColorPickerWindowResources> Resources =
        CommonAXAMLResourceStore<ColorPickerWindowResources>.Create(
            resourceName: "Common color-picker resources",
            static () => new ColorPickerWindowResources(),
            sourceFileName: "ColorPickerWindow.axaml");
#else
    private static readonly Lazy<ColorPickerWindowResources> Resources =
        new(static () => new ColorPickerWindowResources());
#endif

    /// <summary>
    /// Initializes the compiled color-picker resource dictionary.
    /// </summary>
    public ColorPickerWindowResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static ColorPickerWindowResources Current
    {
        get
        {
#if DEBUG
            return Resources.Current;
#else
            return Resources.Value;
#endif
        }
    }
}
