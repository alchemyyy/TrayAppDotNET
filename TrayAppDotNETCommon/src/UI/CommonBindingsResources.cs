using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI;

public sealed partial class CommonBindingsResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<CommonBindingsResources> Resources =
        CommonAXAMLResourceStore<CommonBindingsResources>.Create(
            resourceName: "Common binding resources",
            static () => new CommonBindingsResources(),
            sourceFileName: "CommonBindings.axaml");
#else
    private static readonly Lazy<CommonBindingsResources> Resources =
        new(static () => new CommonBindingsResources());
#endif

    /// <summary>
    /// Initializes the compiled common-binding resource dictionary.
    /// </summary>
    public CommonBindingsResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static CommonBindingsResources Current
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
