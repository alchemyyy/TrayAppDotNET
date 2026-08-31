using Avalonia.Controls;
using Avalonia.Media;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>Reads theme-color pairs from AXAML resource dictionaries.</summary>
public static class AppThemeResourceReader
{
    /// <summary>Resolves a theme color by prefix and property name.</summary>
    public static ThemeColor Color(ResourceDictionary resources, string prefix, string name)
    {
        string normalizedPrefix = prefix.EndsWith('.') ? prefix : prefix + ".";
        string key = normalizedPrefix + name;
        object? value = resources[key];
        if (value is ThemeColor color) return color;

        throw new InvalidOperationException($"Theme color resource '{key}' is missing or not a ThemeColor.");
    }

    /// <summary>Resolves a single color by prefix and property name.</summary>
    public static Color SingleColor(ResourceDictionary resources, string prefix, string name)
    {
        string normalizedPrefix = prefix.EndsWith('.') ? prefix : prefix + ".";
        string key = normalizedPrefix + name;
        object? value = resources[key];
        if (value is Color color) return color;

        throw new InvalidOperationException($"Theme color resource '{key}' is missing or not a Color.");
    }

#if DEBUG
    /// <summary>
    /// Applies present reloaded entries while preserving existing references and omitted fallbacks.
    /// </summary>
    internal static void SynchronizeColors(ResourceDictionary currentResources, ResourceDictionary candidateResources)
    {
        foreach ((object key, object? candidateValue) in candidateResources)
        {
            if (currentResources[key] is ThemeColor currentColor && candidateValue is ThemeColor candidateColor)
            {
                currentColor.LightHex = candidateColor.LightHex;
                currentColor.DarkHex = candidateColor.DarkHex;
                continue;
            }

            currentResources[key] = candidateValue;
        }
    }
#endif
}
