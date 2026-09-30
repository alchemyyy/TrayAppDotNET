using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessSavedSearchTests
{
    [Fact]
    public void NewSearchUsesTheNextOneBasedDefaultName()
    {
        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Add(
            [],
            query: "chrome");

        ProcessSavedSearch savedSearch = Assert.Single(updated);
        Assert.Equal(expected: "Saved Search 1", savedSearch.Name);
        Assert.Equal(expected: "chrome", savedSearch.Query);
    }

    [Fact]
    public void NewSearchAdvancesPastAConflictingDefaultName()
    {
        ProcessSavedSearch first = new() { Name = "Saved Search 1", Query = "alpha" };
        ProcessSavedSearch conflicting = new() { Name = "Saved Search 3", Query = "beta" };

        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Add(
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
        ProcessSavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Add([savedSearch], query);

        ProcessSavedSearch retainedSearch = Assert.Single(updated);
        Assert.Equal(expected: "Browsers", retainedSearch.Name);
        Assert.Equal(expected: "chrome", retainedSearch.Query);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddSkipsAnEmptyQuery(string query)
    {
        ProcessSavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Add([savedSearch], query);

        Assert.Equal(expected: "chrome", Assert.Single(updated).Query);
    }

    [Fact]
    public void AddAppendsAQueryThatIsNotSaved()
    {
        ProcessSavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Add(
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
        ProcessSavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        Assert.True(ProcessSavedSearchCollection.ContainsQuery([savedSearch], query));
    }

    [Theory]
    [InlineData("firefox")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsavedOrEmptyQueryIsNotFound(string query)
    {
        ProcessSavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        Assert.False(ProcessSavedSearchCollection.ContainsQuery([savedSearch], query));
    }

    [Fact]
    public void RegexQueriesDifferingOnlyByEscapeCaseAreDistinct()
    {
        // \d matches digits while \D matches everything else
        ProcessSavedSearch savedSearch = new() { Name = "Digits", Query = "{Name}=~\"^\\d\"" };

        Assert.True(ProcessSavedSearchCollection.ContainsQuery([savedSearch], query: " {Name}=~\"^\\d\" "));
        Assert.False(ProcessSavedSearchCollection.ContainsQuery([savedSearch], query: "{Name}=~\"^\\D\""));
        Assert.Equal(
            expected: 2,
            ProcessSavedSearchCollection.Add([savedSearch], query: "{Name}=~\"^\\D\"").Count);
    }

    [Fact]
    public void RenameTrimsTheNameAndPreservesTheQuery()
    {
        ProcessSavedSearch savedSearch = new() { Name = "Saved Search 1", Query = "chrome" };

        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Rename(
            [savedSearch],
            searchIndex: 0,
            name: "  Browsers  ");

        ProcessSavedSearch renamedSearch = Assert.Single(updated);
        Assert.Equal(expected: "Browsers", renamedSearch.Name);
        Assert.Equal(expected: "chrome", renamedSearch.Query);
    }

    [Fact]
    public void EmptyRenameKeepsTheExistingName()
    {
        ProcessSavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Rename(
            [savedSearch],
            searchIndex: 0,
            name: "   ");

        Assert.Equal(expected: "Browsers", Assert.Single(updated).Name);
    }

    [Theory]
    [InlineData("{Name}=~\"^chrome\"")]
    [InlineData("{Command line} !~ 'helper'")]
    public void RegexComparisonsAreDetected(string query) =>
        Assert.True(ProcessSavedSearchCollection.UsesRegularExpression(query));

    [Theory]
    [InlineData("literal =~ text")]
    [InlineData("{Name}=\"literal =~ text\"")]
    [InlineData("{Name}=chrome")]
    public void RegexLikeTextOutsideARegexComparisonIsIgnored(string query) =>
        Assert.False(ProcessSavedSearchCollection.UsesRegularExpression(query));

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
                    new ProcessSavedSearch { Name = "Browsers", Query = "{Name}=~\"^(chrome|firefox)\\.exe$\"" }
                ]
            };
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            ProcessSavedSearch savedSearch = Assert.Single(loaded.ProcessSavedSearches);
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
            new ProcessSavedSearch { Name = "Saved Search 1", Query = "chrome" }
        ]);

        Assert.Equal(expected: 0, changedCount);
        Assert.Equal([nameof(AppSettings.ProcessSavedSearches)], changedProperties);
        Assert.Equal(expected: "chrome", Assert.Single(settings.ProcessSavedSearches).Query);
    }
}
