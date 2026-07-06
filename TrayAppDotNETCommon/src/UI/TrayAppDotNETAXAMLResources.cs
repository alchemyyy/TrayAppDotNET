using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI;

public static class TrayAppDotNETAXAMLResources
{
    private const string SettingsWindowCommonResourcesUri =
        "avares://TrayAppDotNETCommon/UI/SettingsWindowCommon.axaml";

    private const string CommonBindingsResourcesUri =
        "avares://TrayAppDotNETCommon/UI/CommonBindings.axaml";

    /// <summary>
    /// Adds a compiled resource dictionary for the known AXAML resource URI.
    /// </summary>
    public static void Merge(Control owner, string resourceUri)
    {
        ResourceDictionary dictionary = resourceUri switch
        {
            SettingsWindowCommonResourcesUri => new SettingsWindowCommonResources(),
            CommonBindingsResourcesUri => new CommonBindingsResources(),
            _ => throw new InvalidOperationException($"Unknown AXAML resource dictionary '{resourceUri}'.")
        };

        owner.Resources.MergedDictionaries.Add(dictionary);
    }
}
