using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI;

public sealed partial class SettingsWindowCommonResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<SettingsWindowCommonResources> Resources =
        CommonAXAMLResourceStore<SettingsWindowCommonResources>.Create(
            "Common settings-window resources",
            static () => new SettingsWindowCommonResources(),
            "SettingsWindowCommon.axaml");
#else
    private static readonly Lazy<SettingsWindowCommonResources> Resources =
        new(static () => new SettingsWindowCommonResources());
#endif

    /// <summary>
    /// Initializes the compiled settings-window resource dictionary.
    /// </summary>
    public SettingsWindowCommonResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static SettingsWindowCommonResources Current
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
