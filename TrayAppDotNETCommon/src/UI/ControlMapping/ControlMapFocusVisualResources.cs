using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.ControlMapping;

public sealed partial class ControlMapFocusVisualResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<ControlMapFocusVisualResources> Resources =
        CommonAXAMLResourceStore<ControlMapFocusVisualResources>.Create(
            resourceName: "Common control map focus visual resources",
            static () => new ControlMapFocusVisualResources(),
            sourceFileName: "ControlMapFocusVisual.axaml");
#else
    private static readonly Lazy<ControlMapFocusVisualResources> Resources =
        new(static () => new ControlMapFocusVisualResources());
#endif

    /// <summary>Initializes the compiled focus visual resource dictionary.</summary>
    public ControlMapFocusVisualResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static ControlMapFocusVisualResources Current
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
