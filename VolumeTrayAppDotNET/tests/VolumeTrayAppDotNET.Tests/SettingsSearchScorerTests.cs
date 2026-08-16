using System.Globalization;
using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class SettingsSearchScorerTests
{
    [Fact]
    public void CommonSynonymResourceIsPresentAndParseable()
    {
        string commonDefinitions = Assert.IsType<string>(
            TrayAppDotNETCommon.Localization.CommonStrings.ResourceManager.GetString(
                SettingsSearchSynonymMap.CommonResourceKey,
                CultureInfo.InvariantCulture));
        Assert.False(string.IsNullOrWhiteSpace(commonDefinitions));

        string[] groupDefinitions = commonDefinitions.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(groupDefinitions);
        foreach (string groupDefinition in groupDefinitions)
        {
            string[] terms = groupDefinition.Split(
                '|',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.True(terms.Length >= 2, $"Invalid synonym group: {groupDefinition}");
        }

        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(commonDefinitions);
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Maximum rows before scrolling", "App drawer")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("highest", documents, synonymMap);

        Assert.Contains(1, matches);
    }

    [Fact]
    public void FindMatches_FindsMisspelledCardTitle()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Brightness update rate", "Monitors"),
            new SettingsSearchDocument(2, "Update check interval", "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("brighness", documents);

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

        HashSet<int> matches = SettingsSearchScorer.FindMatches("tehme", documents);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_UsesCharacterNGramsForHeavilyMisspelledTerms()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Environmental controls", "Brightness"),
            new SettingsSearchDocument(2, "Update interval", "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("environxxxxmental", documents);

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

        HashSet<int> matches = SettingsSearchScorer.FindMatches("theme", documents);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_UsesLocalizedSearchKeywordsForConceptualMatches()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(
                1,
                "Run on startup",
                "Start the app automatically.",
                "launch login sign in boot autostart",
                "Startup",
                "General"),
            new SettingsSearchDocument(
                2,
                "Card spacing",
                "Vertical distance between cards.",
                "layout gap margin",
                "Layout",
                "Flyout")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("launch when I sign in", documents);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_UsesLocalizedSynonymsForConceptualMatches()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Max apps per row", "App drawer"),
            new SettingsSearchDocument(2, "Card spacing", "Layout")
        ];
        string commonDefinitions = Assert.IsType<string>(
            TrayAppDotNETCommon.Localization.CommonStrings.ResourceManager.GetString(
                SettingsSearchSynonymMap.CommonResourceKey,
                CultureInfo.InvariantCulture));
        string appDefinitions = Assert.IsType<string>(
            VolumeTrayAppDotNET.Localization.Strings.ResourceManager.GetString(
                SettingsSearchSynonymMap.AppResourceKey,
                CultureInfo.InvariantCulture));
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            commonDefinitions,
            appDefinitions);

        HashSet<int> matches = SettingsSearchScorer.FindMatches("highest", documents, synonymMap);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_HandlesMultiwordSynonymPhrases()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Maximum rows before scrolling", "App drawer"),
            new SettingsSearchDocument(2, "Scroll speed", "Flyout")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "maximum|max|highest|upper limit|upper bound");

        HashSet<int> matches = SettingsSearchScorer.FindMatches("upper limit", documents, synonymMap);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_DoesNotExposeWordsInsideQualifiedSynonyms()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Network adapter", "Network"),
            new SettingsSearchDocument(2, "Card spacing", "Layout")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "network interface|network adapter|NIC|network card|connection adapter");

        HashSet<int> matches = SettingsSearchScorer.FindMatches("card", documents, synonymMap);

        Assert.DoesNotContain(1, matches);
        Assert.Contains(2, matches);
    }

    [Fact]
    public void FindMatches_PreservesLexicalFallbackForUnanchoredSynonymGroups()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(
                1,
                "Installation",
                "Manage the installed application.",
                "setup install uninstall remove application program files",
                "General",
                "General")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "install application|set up application|add application");

        HashSet<int> matches = SettingsSearchScorer.FindMatches(
            "set up application",
            documents,
            synonymMap);

        Assert.Contains(1, matches);
    }

    [Fact]
    public void FindMatches_MergesExtensionsOfTheSameSynonymGroup()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Max apps per row", "App drawer")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "maximum|max|highest|upper limit",
            "maximum|highest|largest|greatest");

        HashSet<int> matches = SettingsSearchScorer.FindMatches("largest", documents, synonymMap);

        Assert.Contains(1, matches);
    }

    [Fact]
    public void FindMatches_DoesNotBridgeGroupsThroughOneAmbiguousPhrase()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Fan jumpstart", "Fan control")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "autostart|run on startup|launch at login",
            "fan jumpstart|run on startup|spin-up boost");

        HashSet<int> matches = SettingsSearchScorer.FindMatches("autostart", documents, synonymMap);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_AllowsOneCardToAnchorMultipleConceptGroups()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Battery below 10%", "Battery trigger")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "battery below|battery under|charge below",
            "battery below|low battery|low charge");

        HashSet<int> comparisonMatches = SettingsSearchScorer.FindMatches(
            "battery under",
            documents,
            synonymMap);
        HashSet<int> lowChargeMatches = SettingsSearchScorer.FindMatches(
            "low charge",
            documents,
            synonymMap);

        Assert.Contains(1, comparisonMatches);
        Assert.Contains(1, lowChargeMatches);
    }

    [Fact]
    public void FindMatches_DoesNotExpandSynonymsInsideASCIIWords()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Maximize the window", "Window")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse("max|highest");

        HashSet<int> matches = SettingsSearchScorer.FindMatches("highest", documents, synonymMap);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_ExpandsLocalizedSynonymsInsideUnsegmentedText()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "最高音量", "音频"),
            new SettingsSearchDocument(2, "主题颜色", "外观")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse("最高|最大");

        HashSet<int> matches = SettingsSearchScorer.FindMatches("最大", documents, synonymMap);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_RestrictsShortQueriesToTokenPrefixes()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Volume", "General"),
            new SettingsSearchDocument(2, "Theme", "Appearance")
        ];
        HashSet<int> matches = SettingsSearchScorer.FindMatches("v", documents);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_IgnoresUnrecognizedNaturalLanguageFiller()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Use dark theme", "Appearance"),
            new SettingsSearchDocument(2, "Check for updates", "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(
            "please make the interface dark",
            documents);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_DownweightsShortIncidentalFillerMatches()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Master volume", "Audio"),
            new SettingsSearchDocument(2, "Theme mode", "Appearance")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("set the volume", documents);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_RequiresCoverageForMultipleMeaningfulTerms()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Theme mode", "Appearance"),
            new SettingsSearchDocument(2, "Update interval", "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("theme interval", documents);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_NormalizesDiacritics()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Luminosité de l'écran", "Affichage"),
            new SettingsSearchDocument(2, "Volume principal", "Audio")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("luminosite", documents);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_FoldsCommonLatinLetters()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Größe der Menüstraße", "Darstellung"),
            new SettingsSearchDocument(2, "Lautstärke", "Audio")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("menustrasse", documents);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_UsesSubstringsForUnsegmentedScripts()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "自动调节屏幕亮度", "显示"),
            new SettingsSearchDocument(2, "主音量", "音频")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("亮度", documents);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
    }

    [Fact]
    public void FindMatches_RecognizesAcronyms()
    {
        SettingsSearchDocument[] documents =
        [
            new SettingsSearchDocument(1, "Graphics processing unit rendering", "Rendering"),
            new SettingsSearchDocument(2, "Software rendering", "Rendering")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches("gpu", documents);

        Assert.Contains(1, matches);
        Assert.DoesNotContain(2, matches);
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
            "No settings match \"{0}\".",
            [new SettingsSearchPageSource("General", page)]);
        IReadOnlyList<SettingsSearchDocument> documents = view.ReadDocuments();
        SettingsSearchDocument startupDocument = Assert.Single(
            documents,
            static document => document.PrimaryText.Contains("Start automatically", StringComparison.Ordinal));

        view.ApplyMatches([startupDocument.ID], "startup");

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
            "No settings match \"{0}\".",
            [new SettingsSearchPageSource("About", page)]);

        view.ApplyMatches([], "monitor");

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
            "No settings match \"{0}\".",
            [new SettingsSearchPageSource("General", page)]);
        SettingsSearchDocument themeDocument = Assert.Single(
            view.ReadDocuments(),
            static document => document.PrimaryText.Contains("Use dark theme", StringComparison.Ordinal));

        view.ApplyMatches([themeDocument.ID], "theme");

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

    [Fact]
    public void SearchView_IndexesExplicitCardKeywordsSeparately()
    {
        Border startupCard = TrayAppDotNETSettingsCards.RegisterSearchCard(
            new Border { Child = new TextBlock { Text = "Run on startup" } },
            "launch login boot");
        StackPanel page = new()
        {
            Children =
            {
                SettingsSearchMetadata.Mark(new TextBlock { Text = "General" }, SettingsSearchRole.PageHeader),
                startupCard
            }
        };
        SettingsSearchView view = new(
            new TextBlock(),
            "No settings match \"{0}\".",
            [new SettingsSearchPageSource("General", page)]);
        SettingsSearchDocument document = Assert.Single(view.ReadDocuments());

        HashSet<int> matches = SettingsSearchScorer.FindMatches("boot", [document]);

        Assert.Equal("launch login boot", document.SearchKeywords);
        Assert.Contains(document.ID, matches);
    }

    private static Border SearchCard(string text) => SettingsSearchMetadata.Mark(
        new Border { Child = new TextBlock { Text = text } },
        SettingsSearchRole.Card);
}
