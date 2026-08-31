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
            "chrome");

        ProcessSavedSearch savedSearch = Assert.Single(updated);
        Assert.Equal("Saved Search 1", savedSearch.Name);
        Assert.Equal("chrome", savedSearch.Query);
    }

    [Fact]
    public void NewSearchAdvancesPastAConflictingDefaultName()
    {
        ProcessSavedSearch first = new() { Name = "Saved Search 1", Query = "alpha" };
        ProcessSavedSearch conflicting = new() { Name = "Saved Search 3", Query = "beta" };

        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Add(
            [first, conflicting],
            "gamma");

        Assert.Equal("Saved Search 4", updated[^1].Name);
    }

    [Fact]
    public void RenameTrimsTheNameAndPreservesTheQuery()
    {
        ProcessSavedSearch savedSearch = new() { Name = "Saved Search 1", Query = "chrome" };

        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Rename(
            [savedSearch],
            searchIndex: 0,
            "  Browsers  ");

        ProcessSavedSearch renamedSearch = Assert.Single(updated);
        Assert.Equal("Browsers", renamedSearch.Name);
        Assert.Equal("chrome", renamedSearch.Query);
    }

    [Fact]
    public void EmptyRenameKeepsTheExistingName()
    {
        ProcessSavedSearch savedSearch = new() { Name = "Browsers", Query = "chrome" };

        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Rename(
            [savedSearch],
            searchIndex: 0,
            "   ");

        Assert.Equal("Browsers", Assert.Single(updated).Name);
    }

    [Theory]
    [InlineData("{Name}=~\"^chrome\"")]
    [InlineData("{Command line} !~ 'helper'")]
    public void RegexComparisonsAreDetected(string query)
    {
        Assert.True(ProcessSavedSearchCollection.UsesRegularExpression(query));
    }

    [Theory]
    [InlineData("literal =~ text")]
    [InlineData("{Name}=\"literal =~ text\"")]
    [InlineData("{Name}=chrome")]
    public void RegexLikeTextOutsideARegexComparisonIsIgnored(string query)
    {
        Assert.False(ProcessSavedSearchCollection.UsesRegularExpression(query));
    }

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
                    new ProcessSavedSearch
                    {
                        Name = "Browsers",
                        Query = "{Name}=~\"^(chrome|firefox)\\.exe$\""
                    }
                ]
            };
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            ProcessSavedSearch savedSearch = Assert.Single(loaded.ProcessSavedSearches);
            Assert.Equal("Browsers", savedSearch.Name);
            Assert.Equal("{Name}=~\"^(chrome|firefox)\\.exe$\"", savedSearch.Query);
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
            new ProcessSavedSearch
            {
                Name = "Saved Search 1",
                Query = "chrome"
            }
        ]);

        Assert.Equal(0, changedCount);
        Assert.Equal([nameof(AppSettings.ProcessSavedSearches)], changedProperties);
        Assert.Equal("chrome", Assert.Single(settings.ProcessSavedSearches).Query);
    }
}
