using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class FlyoutFrameResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<FlyoutFrameResources> Resources =
        CommonAXAMLResourceStore<FlyoutFrameResources>.Create(
            resourceName: "Common flyout-frame resources",
            static () => new FlyoutFrameResources(),
            sourceFileName: "FlyoutFrame.axaml");
#else
    private static readonly Lazy<FlyoutFrameResources> Resources =
        new(static () => new FlyoutFrameResources());
#endif

    /// <summary>Initializes the compiled flyout-frame resource dictionary.</summary>
    public FlyoutFrameResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static FlyoutFrameResources Current
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
