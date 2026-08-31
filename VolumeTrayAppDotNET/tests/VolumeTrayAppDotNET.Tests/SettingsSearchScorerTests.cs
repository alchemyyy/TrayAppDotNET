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
                nameof(TrayAppDotNETCommon.Localization.CommonStrings.SettingsWindow_SearchSynonymGroups_Common),
                CultureInfo.InvariantCulture));
        Assert.False(string.IsNullOrWhiteSpace(commonDefinitions));

        string[] groupDefinitions = commonDefinitions.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(groupDefinitions);
        foreach (string groupDefinition in groupDefinitions)
        {
            string[] terms = groupDefinition.Split(
                separator: '|',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.True(terms.Length >= 2, $"Invalid synonym group: {groupDefinition}");
        }

        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(commonDefinitions);
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Maximum rows before scrolling", contextText: "App drawer")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "highest", documents, synonymMap);

        Assert.Contains(expected: 1, matches);
    }

    [Fact]
    public void FindMatches_FindsMisspelledCardTitle()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Brightness update rate", contextText: "Monitors"),
            new(id: 2, primaryText: "Update check interval", contextText: "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "brighness", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_FindsAdjacentTransposition()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Theme mode", contextText: "Appearance"),
            new(id: 2, primaryText: "Update interval", contextText: "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "tehme", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_UsesCharacterNGramsForHeavilyMisspelledTerms()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Environmental controls", contextText: "Brightness"),
            new(id: 2, primaryText: "Update interval", contextText: "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "environxxxxmental", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_UsesPageAndSubsectionContext()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Context menu font size", contextText: "Theme. Appearance"),
            new(id: 2, primaryText: "Update check interval", contextText: "About. Updates")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "theme", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_UsesLocalizedSearchKeywordsForConceptualMatches()
    {
        SettingsSearchDocument[] documents =
        [
            new(
                id: 1,
                titleText: "Run on startup",
                bodyText: "Start the app automatically.",
                searchKeywords: "launch login sign in boot autostart",
                subsectionText: "Startup",
                pageText: "General"),
            new(
                id: 2,
                titleText: "Card spacing",
                bodyText: "Vertical distance between cards.",
                searchKeywords: "layout gap margin",
                subsectionText: "Layout",
                pageText: "Flyout")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "launch when I sign in", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_UsesLocalizedSynonymsForConceptualMatches()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Max apps per row", contextText: "App drawer"),
            new(id: 2, primaryText: "Card spacing", contextText: "Layout")
        ];
        string commonDefinitions = Assert.IsType<string>(
            TrayAppDotNETCommon.Localization.CommonStrings.ResourceManager.GetString(
                nameof(TrayAppDotNETCommon.Localization.CommonStrings.SettingsWindow_SearchSynonymGroups_Common),
                CultureInfo.InvariantCulture));
        string appDefinitions = Assert.IsType<string>(
            Localization.Strings.ResourceManager.GetString(
                nameof(Localization.Strings.SettingsWindow_SearchSynonymGroups_App),
                CultureInfo.InvariantCulture));
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            commonDefinitions,
            appDefinitions);

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "highest", documents, synonymMap);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_HandlesMultiwordSynonymPhrases()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Maximum rows before scrolling", contextText: "App drawer"),
            new(id: 2, primaryText: "Scroll speed", contextText: "Flyout")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "maximum|max|highest|upper limit|upper bound");

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "upper limit", documents, synonymMap);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_DoesNotExposeWordsInsideQualifiedSynonyms()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Network adapter", contextText: "Network"),
            new(id: 2, primaryText: "Card spacing", contextText: "Layout")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "network interface|network adapter|NIC|network card|connection adapter");

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "card", documents, synonymMap);

        Assert.DoesNotContain(expected: 1, matches);
        Assert.Contains(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_PreservesLexicalFallbackForUnanchoredSynonymGroups()
    {
        SettingsSearchDocument[] documents =
        [
            new(
                id: 1,
                titleText: "Installation",
                bodyText: "Manage the installed application.",
                searchKeywords: "setup install uninstall remove application program files",
                subsectionText: "General",
                pageText: "General")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "install application|set up application|add application");

        HashSet<int> matches = SettingsSearchScorer.FindMatches(
            query: "set up application",
            documents,
            synonymMap);

        Assert.Contains(expected: 1, matches);
    }

    [Fact]
    public void FindMatches_MergesExtensionsOfTheSameSynonymGroup()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Max apps per row", contextText: "App drawer")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "maximum|max|highest|upper limit",
            "maximum|highest|largest|greatest");

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "largest", documents, synonymMap);

        Assert.Contains(expected: 1, matches);
    }

    [Fact]
    public void FindMatches_DoesNotBridgeGroupsThroughOneAmbiguousPhrase()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Fan jumpstart", contextText: "Fan control")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "autostart|run on startup|launch at login",
            "fan jumpstart|run on startup|spin-up boost");

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "autostart", documents, synonymMap);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_AllowsOneCardToAnchorMultipleConceptGroups()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Battery below 10%", contextText: "Battery trigger")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse(
            "battery below|battery under|charge below",
            "battery below|low battery|low charge");

        HashSet<int> comparisonMatches = SettingsSearchScorer.FindMatches(
            query: "battery under",
            documents,
            synonymMap);
        HashSet<int> lowChargeMatches = SettingsSearchScorer.FindMatches(
            query: "low charge",
            documents,
            synonymMap);

        Assert.Contains(expected: 1, comparisonMatches);
        Assert.Contains(expected: 1, lowChargeMatches);
    }

    [Fact]
    public void FindMatches_DoesNotExpandSynonymsInsideASCIIWords()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Maximize the window", contextText: "Window")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse("max|highest");

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "highest", documents, synonymMap);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_ExpandsLocalizedSynonymsInsideUnsegmentedText()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "最高音量", contextText: "音频"),
            new(id: 2, primaryText: "主题颜色", contextText: "外观")
        ];
        SettingsSearchSynonymMap synonymMap = SettingsSearchSynonymMap.Parse("最高|最大");

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "最大", documents, synonymMap);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_RestrictsShortQueriesToTokenPrefixes()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Volume", contextText: "General"),
            new(id: 2, primaryText: "Theme", contextText: "Appearance")
        ];
        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "v", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_IgnoresUnrecognizedNaturalLanguageFiller()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Use dark theme", contextText: "Appearance"),
            new(id: 2, primaryText: "Check for updates", contextText: "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(
            query: "please make the interface dark",
            documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_DownweightsShortIncidentalFillerMatches()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Master volume", contextText: "Audio"),
            new(id: 2, primaryText: "Theme mode", contextText: "Appearance")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "set the volume", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_RequiresCoverageForMultipleMeaningfulTerms()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Theme mode", contextText: "Appearance"),
            new(id: 2, primaryText: "Update interval", contextText: "About")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "theme interval", documents);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_NormalizesDiacritics()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Luminosité de l'écran", contextText: "Affichage"),
            new(id: 2, primaryText: "Volume principal", contextText: "Audio")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "luminosite", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_FoldsCommonLatinLetters()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Größe der Menüstraße", contextText: "Darstellung"),
            new(id: 2, primaryText: "Lautstärke", contextText: "Audio")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "menustrasse", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_UsesSubstringsForUnsegmentedScripts()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "自动调节屏幕亮度", contextText: "显示"),
            new(id: 2, primaryText: "主音量", contextText: "音频")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "亮度", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
    }

    [Fact]
    public void FindMatches_RecognizesAcronyms()
    {
        SettingsSearchDocument[] documents =
        [
            new(id: 1, primaryText: "Graphics processing unit rendering", contextText: "Rendering"),
            new(id: 2, primaryText: "Software rendering", contextText: "Rendering")
        ];

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "gpu", documents);

        Assert.Contains(expected: 1, matches);
        Assert.DoesNotContain(expected: 2, matches);
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
            Children =
            {
                pageHeader,
                startupHeader,
                startupCard,
                appearanceHeader,
                themeCard
            }
        };
        TextBlock status = new();
        SettingsSearchView view = new(
            status,
            noMatchesFormat: "No settings match \"{0}\".",
            [new SettingsSearchPageSource(Label: "General", page)]);
        IReadOnlyList<SettingsSearchDocument> documents = view.ReadDocuments();
        SettingsSearchDocument startupDocument = Assert.Single(
            documents,
            static document => document.PrimaryText.Contains(value: "Start automatically", StringComparison.Ordinal));

        view.ApplyMatches([startupDocument.ID], query: "startup");

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
            noMatchesFormat: "No settings match \"{0}\".",
            [new SettingsSearchPageSource(Label: "About", page)]);

        view.ApplyMatches([], query: "monitor");

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
            noMatchesFormat: "No settings match \"{0}\".",
            [new SettingsSearchPageSource(Label: "General", page)]);
        SettingsSearchDocument themeDocument = Assert.Single(
            view.ReadDocuments(),
            static document => document.PrimaryText.Contains(value: "Use dark theme", StringComparison.Ordinal));

        view.ApplyMatches([themeDocument.ID], query: "theme");

        Assert.True(compositeLayout.IsVisible);
        Assert.False(startupCard.IsVisible);
        Assert.True(themeCard.IsVisible);
    }

    [Fact]
    public void TextExtractorPreservesTitleFirstVisualOrder()
    {
        StackPanel text = new()
        {
            Children = { new TextBlock { Text = "Card title" }, new TextBlock { Text = "Card description" } }
        };
        Grid cardLayout = new() { Children = { text, new TextBlock { Text = "Control value" } } };

        string extracted = SettingsSearchTextExtractor.Read([cardLayout]);

        Assert.Equal(expected: "Card title. Card description. Control value", extracted);
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
            noMatchesFormat: "No settings match \"{0}\".",
            [new SettingsSearchPageSource(Label: "General", page)]);
        SettingsSearchDocument document = Assert.Single(view.ReadDocuments());

        HashSet<int> matches = SettingsSearchScorer.FindMatches(query: "boot", [document]);

        Assert.Equal(expected: "launch login boot", document.SearchKeywords);
        Assert.Contains(document.ID, matches);
    }

    private static Border SearchCard(string text) => SettingsSearchMetadata.Mark(
        new Border { Child = new TextBlock { Text = text } },
        SettingsSearchRole.Card);
}
