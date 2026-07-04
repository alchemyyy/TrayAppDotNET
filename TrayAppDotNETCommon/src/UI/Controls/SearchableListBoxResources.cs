using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class SearchableListBoxResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled searchable-list resource dictionary.
    /// </summary>
    public SearchableListBoxResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
