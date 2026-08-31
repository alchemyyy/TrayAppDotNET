using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class SearchableListBoxResources : ResourceDictionary
{
#if DEBUG
    private static readonly CommonAXAMLResourceStore<SearchableListBoxResources> Resources =
        CommonAXAMLResourceStore<SearchableListBoxResources>.Create(
            resourceName: "Common searchable-list resources",
            static () => new SearchableListBoxResources(),
            sourceFileName: "SearchableListBox.axaml");
#else
    private static readonly Lazy<SearchableListBoxResources> Resources =
        new(static () => new SearchableListBoxResources());
#endif

    /// <summary>
    /// Initializes the compiled searchable-list resource dictionary.
    /// </summary>
    public SearchableListBoxResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static SearchableListBoxResources Current
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
