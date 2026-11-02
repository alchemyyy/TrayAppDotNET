using System.Globalization;
using System.Resources;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.UI.Settings;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

/// <summary>
/// An app's own synonym groups extend the shared ones. The app resources are the copies this project embeds (see the
/// csproj), so the scorer and the app's groups are checked together without app code.
/// </summary>
public sealed class AppSearchSynonymTests
{
    [Fact]
    public void FindMatches_UsesVolumeSynonymsForConceptualMatches()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Max apps per row", contextText: "App drawer"),
            new(id: 2, primaryText: "Card spacing", contextText: "Layout")
        ];
        string commonDefinitions = Assert.IsType<string>(
            CommonStrings.ResourceManager.GetString(
                nameof(CommonStrings.SettingsWindow_SearchSynonymGroups_Common),
                CultureInfo.InvariantCulture));
        string appDefinitions = Assert.IsType<string>(
            new ResourceManager("AppResources.Volume", typeof(AppSearchSynonymTests).Assembly).GetString(
                // Volume's Strings.resx is embedded under a LogicalName ReSharper does not map back to the file
                // ReSharper disable once ResourceItemNotResolved
                "SettingsWindow_SearchSynonymGroups_App",
                CultureInfo.InvariantCulture));
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            commonDefinitions,
            appDefinitions);

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "highest", documents, synonymMap);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }
}
