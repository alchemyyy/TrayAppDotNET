using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI;

public static class TrayAppDotNETAXAMLResources
{
    /// <summary>
    /// Loads a resource dictionary from AXAML and merges it into the owner.
    /// </summary>
    public static void Merge(Control owner, string resourceUri)
    {
        Uri uri = new(resourceUri, UriKind.Absolute);
        object? loaded = AvaloniaXamlLoader.Load(uri);
        if (loaded is ResourceDictionary dictionary)
        {
            owner.Resources.MergedDictionaries.Add(dictionary);
            return;
        }

        throw new InvalidOperationException($"AXAML resource '{resourceUri}' is not a ResourceDictionary.");
    }
}
