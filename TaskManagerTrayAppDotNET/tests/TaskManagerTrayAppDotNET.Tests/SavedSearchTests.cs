using TaskManagerTrayAppDotNET.Models;
using TrayAppDotNETCommon.Serialization;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SavedSearchTests
{
    [Fact]
    public void NewSearchUsesTheNextOneBasedDefaultName()
    {
        List<SavedSearch> updated = SavedSearchCollection.Add(
            [],
            query: "chrome");

        SavedSearch savedSearch = Assert.Single(updated);
        Assert.Equal(expected: "Saved Search 1", savedSearch.Name);
        Assert.Equal(expected: "chrome", savedSearch.Query);
    }

    [Fact]
    public void NewSearchAdvancesPastAConflictingDefaultName()
    {
        SavedSearch first = new() { Name = "Saved Search 1", Query = "alpha" };
        SavedSearch conflicting = new() { Name = "Saved Search 3", Query = "beta" };

        List<SavedSearch> updated = SavedSearchCollection.Add(
            [first, conflicting],
            query: "gamma");

        Assert.Equal(expected: "Saved Search 4", updated[^1].Name);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("  chrome  ")]
    [InlineData("CHROME")]
    public void AddSkipsAQueryThatIsAlreadySaved(string query)
    {
        SavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        List<SavedSearch> updated = SavedSearchCollection.Add([savedSearch], query);

        SavedSearch retainedSearch = Assert.Single(updated);
        Assert.Equal(expected: "Browsers", retainedSearch.Name);
        Assert.Equal(expected: "chrome", retainedSearch.Query);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddSkipsAnEmptyQuery(string query)
    {
        SavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        List<SavedSearch> updated = SavedSearchCollection.Add([savedSearch], query);

        Assert.Equal(expected: "chrome", Assert.Single(updated).Query);
    }

    [Fact]
    public void AddAppendsAQueryThatIsNotSaved()
    {
        SavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        List<SavedSearch> updated = SavedSearchCollection.Add(
            [savedSearch],
            query: "firefox");

        Assert.Equal(["chrome", "firefox"], updated.Select(static search => search.Query));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("  chrome  ")]
    [InlineData("CHROME")]
    public void SavedQueryIsFoundIgnoringPaddingAndCase(string query)
    {
        SavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        Assert.True(SavedSearchCollection.ContainsQuery([savedSearch], query));
    }

    [Theory]
    [InlineData("firefox")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsavedOrEmptyQueryIsNotFound(string query)
    {
        SavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        Assert.False(SavedSearchCollection.ContainsQuery([savedSearch], query));
    }

    [Fact]
    public void RegexQueriesDifferingOnlyByEscapeCaseAreDistinct()
    {
        // \d matches digits while \D matches everything else
        SavedSearch savedSearch = new() { Name = "Digits", Query = "{Name}=~\"^\\d\"" };

        Assert.True(SavedSearchCollection.ContainsQuery([savedSearch], query: " {Name}=~\"^\\d\" "));
        Assert.False(SavedSearchCollection.ContainsQuery([savedSearch], query: "{Name}=~\"^\\D\""));
        Assert.Equal(
            expected: 2,
            SavedSearchCollection.Add([savedSearch], query: "{Name}=~\"^\\D\"").Count);
    }

    [Fact]
    public void RenameTrimsTheNameAndPreservesTheQuery()
    {
        SavedSearch savedSearch = new() { Name = "Saved Search 1", Query = "chrome" };

        List<SavedSearch> updated = SavedSearchCollection.Rename(
            [savedSearch],
            searchIndex: 0,
            name: "  Browsers  ");

        SavedSearch renamedSearch = Assert.Single(updated);
        Assert.Equal(expected: "Browsers", renamedSearch.Name);
        Assert.Equal(expected: "chrome", renamedSearch.Query);
    }

    [Fact]
    public void EmptyRenameKeepsTheExistingName()
    {
        SavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        List<SavedSearch> updated = SavedSearchCollection.Rename(
            [savedSearch],
            searchIndex: 0,
            name: "   ");

        Assert.Equal(expected: "Browsers", Assert.Single(updated).Name);
    }

    [Theory]
    [InlineData("{Name}=~\"^chrome\"")]
    [InlineData("{Command line} !~ 'helper'")]
    public void RegexComparisonsAreDetected(string query) =>
        Assert.True(SavedSearchCollection.UsesRegularExpression(query));

    [Theory]
    [InlineData("literal =~ text")]
    [InlineData("{Name}=\"literal =~ text\"")]
    [InlineData("{Name}=chrome")]
    public void RegexLikeTextOutsideARegexComparisonIsIgnored(string query) =>
        Assert.False(SavedSearchCollection.UsesRegularExpression(query));

    [Fact]
    public void SavedSearchesRoundTripThroughSettingsXml()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"TaskManagerTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            AppSettings settings = new()
            {
                Autosave = false,
                ProcessSavedSearches =
                [
                    new SavedSearch { Name = "Browsers", Query = "{Name}=~\"^(chrome|firefox)\\.exe$\"" }
                ]
            };
            TrayXmlSerializer.WriteFile(path, settings);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            SavedSearch savedSearch = Assert.Single(loaded.ProcessSavedSearches);
            Assert.Equal(expected: "Browsers", savedSearch.Name);
            Assert.Equal(expected: "{Name}=~\"^(chrome|firefox)\\.exe$\"", savedSearch.Query);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LiveSavedSearchUpdatesDoNotRaiseAGlobalSettingsRefresh()
    {
        AppSettings settings = new() { Autosave = false };
        List<string?> changedProperties = [];
        int changedCount = 0;
        settings.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);
        settings.Changed += () => changedCount++;

        settings.UpdateProcessSavedSearches(
        [
            new SavedSearch { Name = "Saved Search 1", Query = "chrome" }
        ]);

        Assert.Equal(expected: 0, changedCount);
        Assert.Equal([nameof(AppSettings.ProcessSavedSearches)], changedProperties);
        Assert.Equal(expected: "chrome", Assert.Single(settings.ProcessSavedSearches).Query);
    }
}
