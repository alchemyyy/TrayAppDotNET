using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class SettingsSearchScorerTests
{
    [Fact]
    public void FindMatches_FindsMisspelledCardTitle()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Brightness update rate", "Monitors"),
            new SettingsSearchDocument(2, "Update check interval", "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("brighness", documents, null);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_FindsAdjacentTransposition()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Theme mode", "Appearance"),
            new SettingsSearchDocument(2, "Update interval", "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("tehme", documents, null);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_UsesPageAndSubsectionContext()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Context menu font size", "Theme. Appearance"),
            new SettingsSearchDocument(2, "Update check interval", "About. Updates")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("theme", documents, null);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_UsesSemanticScoresForConceptualMatches()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Start automatically", "General"),
            new SettingsSearchDocument(2, "Card spacing", "Flyout. Layout")
        ];
        Dictionary<int, float> semanticSimilarities = new()
        {
            [1] = 0.71f,
            [2] = 0.37f
        };

        HashSet<int> matches = SettingsSearchScorer.FindMatches(
            "launch when I sign in",
            documents,
            semanticSimilarities);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_DoesNotUseNoisySemanticScoresForShortQueries()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Volume", "General"),
            new SettingsSearchDocument(2, "Theme", "Appearance")
        ];
        Dictionary<int, float> semanticSimilarities = new()
        {
            [1] = 0.90f,
            [2] = 0.89f
        };

        HashSet<int> matches = SettingsSearchScorer.FindMatches("v", documents, semanticSimilarities);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public async Task SemanticEngine_ScoresRelatedSettingsAboveUnrelatedSettings()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Choose the audio output speaker volume", "Devices"),
            new SettingsSearchDocument(2, "Check GitHub for application updates", "About")
        ];
        using SettingsSemanticSearchEngine engine = new();

        IReadOnlyDictionary<int, float> scores = await engine.ScoreAsync(
            "change sound device",
            documents,
            CancellationToken.None);

        Assert.True(scores[1] > scores[2]);
    }

    [Fact]
    public void SearchView_HidesUnmatchedCardsAndEmptySubsections()
    {
        TextBlock pageHeader = SettingsSearchMetadata.Mark(
            new TextBlock { Text = "General" },
            SettingsSearchRole.PageHeader);
        TextBlock startupHeader = SettingsSearchMetadata.Mark(
            new TextBlock { Text = "Startup" },
            SettingsSearchRole.SubsectionHeader);
        Border startupCard = SearchCard("Start automatically");
        TextBlock appearanceHeader = SettingsSearchMetadata.Mark(
            new TextBlock { Text = "Appearance" },
            SettingsSearchRole.SubsectionHeader);
        Border themeCard = SearchCard("Use dark theme");
        StackPanel page = new()
        {
            Children = { pageHeader, startupHeader, startupCard, appearanceHeader, themeCard }
        };
        TextBlock status = new();
        SettingsSearchView view = new(
            status,
            "Finding semantic matches...",
            "No settings match \"{0}\".",
            [new SettingsSearchPageSource("General", page)]);
        IReadOnlyList<SettingsSearchDocument> documents = view.ReadDocuments();
        SettingsSearchDocument startupDocument = Assert.Single(
            documents,
            static document => document.PrimaryText.Contains("Start automatically", StringComparison.Ordinal));

        view.ApplyMatches([startupDocument.ID], "startup", isFinal: true);

        Assert.True(page.IsVisible);
        Assert.True(pageHeader.IsVisible);
        Assert.True(startupHeader.IsVisible);
        Assert.True(startupCard.IsVisible);
        Assert.False(appearanceHeader.IsVisible);
        Assert.False(themeCard.IsVisible);
        Assert.False(status.IsVisible);
    }

    [Fact]
    public void SearchView_HidesPageWhenItHasNoMatchingContent()
    {
        TextBlock pageHeader = SettingsSearchMetadata.Mark(
            new TextBlock { Text = "About" },
            SettingsSearchRole.PageHeader);
        TextBlock author = new() { Text = "Author Example Publisher" };
        StackPanel page = new() { Children = { pageHeader, author } };
        TextBlock status = new();
        SettingsSearchView view = new(
            status,
            "Finding semantic matches...",
            "No settings match \"{0}\".",
            [new SettingsSearchPageSource("About", page)]);

        view.ApplyMatches([], "monitor", isFinal: true);

        Assert.False(page.IsVisible);
        Assert.False(pageHeader.IsVisible);
        Assert.True(status.IsVisible);
    }

    [Fact]
    public void SearchView_FiltersCustomCardsInsideCompositeLayouts()
    {
        Border startupCard = TrayAppDotNETSettingsCards.RegisterSearchCard(
            new Border { Child = new TextBlock { Text = "Start automatically" } });
        Border themeCard = TrayAppDotNETSettingsCards.RegisterSearchCard(
            new Border { Child = new TextBlock { Text = "Use dark theme" } });
        Grid compositeLayout = new() { Children = { startupCard, themeCard } };
        StackPanel page = new()
        {
            Children =
            {
                SettingsSearchMetadata.Mark(new TextBlock { Text = "General" }, SettingsSearchRole.PageHeader),
                compositeLayout
            }
        };
        SettingsSearchView view = new(
            new TextBlock(),
            "Finding semantic matches...",
            "No settings match \"{0}\".",
            [new SettingsSearchPageSource("General", page)]);
        SettingsSearchDocument themeDocument = Assert.Single(
            view.ReadDocuments(),
            static document => document.PrimaryText.Contains("Use dark theme", StringComparison.Ordinal));

        view.ApplyMatches([themeDocument.ID], "theme", isFinal: true);

        Assert.True(compositeLayout.IsVisible);
        Assert.False(startupCard.IsVisible);
        Assert.True(themeCard.IsVisible);
    }

    [Fact]
    public void TextExtractorPreservesTitleFirstVisualOrder()
    {
        StackPanel text = new()
        {
            Children =
            {
                new TextBlock { Text = "Card title" },
                new TextBlock { Text = "Card description" }
            }
        };
        Grid cardLayout = new()
        {
            Children =
            {
                text,
                new TextBlock { Text = "Control value" }
            }
        };

        string extracted = SettingsSearchTextExtractor.Read([cardLayout]);

        Assert.Equal("Card title. Card description. Control value", extracted);
    }

    private static Border SearchCard(string text) => SettingsSearchMetadata.Mark(
        new Border { Child = new TextBlock { Text = text } },
        SettingsSearchRole.Card);
}
