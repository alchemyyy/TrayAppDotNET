using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class SettingsUIResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<SettingsUIResources> Resources =
        CommonAXAMLResourceStore<SettingsUIResources>.Create(
            resourceName: "Common settings UI resources",
            static () => new SettingsUIResources(),
            sourceFileName: "SettingsUI.axaml");
#else
    private static readonly Lazy<SettingsUIResources> Resources =
        new(static () => new SettingsUIResources());
#endif

    /// <summary>
    /// Initializes the compiled settings-UI resource dictionary.
    /// </summary>
    public SettingsUIResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static SettingsUIResources Current
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
