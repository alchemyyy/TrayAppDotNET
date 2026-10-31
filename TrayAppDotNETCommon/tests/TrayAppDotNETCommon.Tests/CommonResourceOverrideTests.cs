using System.Collections;
using System.Globalization;
using System.Resources;
using TrayAppDotNETCommon.Localization;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

/// <summary>
/// LocalizationManager reads an app's resources before CommonStrings, so an app key with a Common name silently
/// replaces the shared text in that app. Every such override must be deliberate and listed here.
/// </summary>
public sealed class CommonResourceOverrideTests
{
    /// <summary>The app keys allowed to replace a CommonStrings entry, with the reason each one differs.</summary>
    private static readonly Dictionary<(string App, string Key), string> AllowedOverrides = new()
    {
        // Brightness ships a default Full bright binding (Ctrl+Alt+Win+F); every other app starts with all
        // actions unbound, which the Common description says
        [("Brightness", "Settings_Hotkeys_SectionDescription")] =
            "Brightness has a default hotkey, so the Common 'All actions start unbound' sentence is wrong there"
    };

    public static TheoryData<string> Apps =>
        ["Battery", "Brightness", "FanControl", "Network", "TaskManager", "Volume"];

    [Theory]
    [MemberData(nameof(Apps))]
    public void AppResourcesOnlyOverrideAllowedCommonKeys(string app)
    {
        HashSet<string> commonKeys = Keys(CommonStrings.ResourceManager);
        HashSet<string> appKeys = Keys(new ResourceManager($"AppResources.{app}", typeof(CommonResourceOverrideTests).Assembly));
        Assert.NotEmpty(appKeys);

        string[] unexpected =
        [
            .. appKeys
                .Where(key => commonKeys.Contains(key) && !AllowedOverrides.ContainsKey((app, key)))
                .Order(StringComparer.Ordinal)
        ];
        Assert.True(
            unexpected.Length == 0,
            $"{app} redefines CommonStrings keys: {string.Join(", ", unexpected)}. Delete them from the app resx, "
            + $"or add each to {nameof(AllowedOverrides)} with the reason the app must differ.");
    }

    [Fact]
    public void EveryAllowedOverrideStillOverridesACommonKey()
    {
        HashSet<string> commonKeys = Keys(CommonStrings.ResourceManager);
        foreach ((string app, string key) in AllowedOverrides.Keys)
        {
            HashSet<string> appKeys = Keys(new ResourceManager($"AppResources.{app}", typeof(CommonResourceOverrideTests).Assembly));
            Assert.Contains(key, appKeys);
            Assert.Contains(key, commonKeys);
        }
    }

    private static HashSet<string> Keys(ResourceManager resources)
    {
        ResourceSet set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
                          ?? throw new InvalidOperationException($"{resources.BaseName} has no neutral resource set.");
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set)
            keys.Add((string)entry.Key);
        return keys;
    }
}
