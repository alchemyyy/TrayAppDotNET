using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class DialogChromeResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<DialogChromeResources> Resources =
        CommonAXAMLResourceStore<DialogChromeResources>.Create(
            resourceName: "Common dialog-chrome resources",
            static () => new DialogChromeResources(),
            sourceFileName: "DialogChrome.axaml");
#else
    private static readonly Lazy<DialogChromeResources> Resources =
        new(static () => new DialogChromeResources());
#endif

    /// <summary>
    /// Initializes the compiled dialog-chrome resource dictionary.
    /// </summary>
    public DialogChromeResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static DialogChromeResources Current
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
