using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SettingsWindowLifetimeTests
{
    [Fact]
    public void FullWorkAreaAxisUsesSharpCorners()
    {
        PixelRect workArea = new(0, 0, 1920, 1040);

        Assert.False(SettingsWindowCommon<TestPage>.SpansFullWorkAreaAxis(
            new PixelRect(200, 150, 960, 670),
            workArea));
        Assert.True(SettingsWindowCommon<TestPage>.SpansFullWorkAreaAxis(
            new PixelRect(300, 0, 900, 1040),
            workArea));
        Assert.True(SettingsWindowCommon<TestPage>.SpansFullWorkAreaAxis(
            new PixelRect(300, -8, 900, 1056),
            workArea));
        Assert.True(SettingsWindowCommon<TestPage>.SpansFullWorkAreaAxis(
            new PixelRect(0, 200, 1920, 600),
            workArea));
        Assert.False(SettingsWindowCommon<TestPage>.SpansFullWorkAreaAxis(
            new PixelRect(2, 2, 1916, 1036),
            workArea));
    }

    [Fact]
    public void SettingsWindowUsesOnlyNativeResizeBorderWithCustomChrome() => AvaloniaTestHost.Run(() =>
    {
        TestSettingsWindow window = new();
        Border root = Assert.IsType<Border>(window.Content);
        Border contentSurface = Assert.IsType<Border>(root.Child);

        Assert.Equal(WindowDecorations.BorderOnly, window.WindowDecorations);
        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.True(window.CanResize);
        Assert.Equal(new Thickness(0), root.BorderThickness);
        Assert.Equal(new Thickness(0), contentSurface.Margin);
    });

    [Theory]
    [InlineData(true, 749.0, true)]
    [InlineData(true, 750.0, false)]
    [InlineData(false, 749.0, false)]
    public void CommonShellOwnsResponsiveSidebarCollapse(
        bool isEnabled,
        double windowWidth,
        bool expectedCollapsed) => AvaloniaTestHost.Run(() =>
    {
        ResponsiveSettingsWindow window = new(isEnabled, windowWidth);
        Border root = Assert.IsType<Border>(window.Content);
        Border contentSurface = Assert.IsType<Border>(root.Child);
        Grid shell = Assert.IsType<Grid>(contentSurface.Child);
        Grid body = Assert.Single(shell.Children.OfType<Grid>(), child => Grid.GetRow(child) == 1);
        Grid sidebar = Assert.Single(body.Children.OfType<Grid>(), child => Grid.GetColumn(child) == 0);

        Assert.Equal(!expectedCollapsed, sidebar.IsVisible);
        Assert.Equal(expectedCollapsed ? 0 : window.ConfiguredSidebarWidth, body.ColumnDefinitions[0].Width.Value);
    });

    [Theory]
    [InlineData(0, 230, 230)]
    [InlineData(double.NaN, 230, 230)]
    [InlineData(90, 230, 140)]
    [InlineData(600, 230, 520)]
    [InlineData(310, 230, 310)]
    public void SidebarWidthResolutionUsesDefaultSentinelAndBounds(
        double persistedWidth,
        double defaultWidth,
        double expectedWidth)
    {
        double width = SettingsSidebarWidthLayout.ResolvePersistedWidth(
            persistedWidth,
            defaultWidth,
            minimumWidth: 140,
            maximumWidth: 520);

        Assert.Equal(expectedWidth, width);
    }

    [Theory]
    [InlineData(960, 520)]
    [InlineData(720, 400)]
    [InlineData(400, 140)]
    public void SidebarMaximumWidthPreservesMinimumContentWidth(double windowWidth, double expectedWidth)
    {
        double width = SettingsSidebarWidthLayout.GetAvailableMaximumWidth(
            windowWidth,
            minimumWidth: 140,
            maximumWidth: 520,
            minimumContentWidth: 320);

        Assert.Equal(expectedWidth, width);
    }

    [Fact]
    public void CtrlDragPersistsSidebarWidthAndCtrlRightClickResetsIt() => AvaloniaTestHost.Run(() =>
    {
        SidebarResizeSettingsWindow window = new();
        window.Show();
        window.UpdateLayout();

        SettingsSidebarResizeHandle resizeHandle = Assert.Single(
            window.GetVisualDescendants().OfType<SettingsSidebarResizeHandle>());
        Assert.False(resizeHandle.IsHitTestVisible);
        Assert.Equal(230, window.DisplayedSidebarWidth);

        Point dragStart = GetWindowPoint(window, resizeHandle);
        window.MouseMove(dragStart, RawInputModifiers.None);
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.LeftCtrl
        });

        Assert.True(resizeHandle.IsHitTestVisible);
        Assert.Same(TrayAppDotNETCursors.SizeWestEast, resizeHandle.Cursor);

        Point dragEnd = new(dragStart.X + 60, dragStart.Y);
        window.MouseDown(dragStart, MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(dragEnd, RawInputModifiers.Control);
        window.MouseUp(dragEnd, MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal(290, window.PersistedSidebarWidth);
        Assert.Equal(290, window.DisplayedSidebarWidth);
        Assert.Equal(1, window.SaveCount);

        Point resetPoint = GetWindowPoint(window, resizeHandle);
        window.MouseMove(resetPoint, RawInputModifiers.Control);
        window.MouseDown(resetPoint, MouseButton.Right, RawInputModifiers.Control);
        window.MouseUp(resetPoint, MouseButton.Right, RawInputModifiers.Control);

        Assert.Equal(0, window.PersistedSidebarWidth);
        Assert.Equal(230, window.DisplayedSidebarWidth);
        Assert.Equal(2, window.SaveCount);

        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Key = Key.LeftCtrl
        });
        Assert.False(resizeHandle.IsHitTestVisible);
        window.Close();

        return;

        static Point GetWindowPoint(Window owner, Control control)
        {
            Point controlCenter = new(control.Bounds.Width / 2, control.Bounds.Height / 2);
            return control.TranslatePoint(controlCenter, owner)
                   ?? throw new InvalidOperationException("Resize handle is not attached to the test window.");
        }
    });

    [Fact]
    public void FailedPageBuildKeepsPreviousPageGenerationActive() => AvaloniaTestHost.Run(() =>
    {
        TestSettingsWindow window = new();
        object stableDataContext = window.StablePage.DataContext!;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(window.SelectFailingPage);

        Assert.Equal("expected page failure", exception.Message);
        Assert.Equal(TestPage.Stable, window.SelectedPage);
        Assert.Same(stableDataContext, window.StablePage.DataContext);
        Assert.Equal(1, window.FailedPageCleanupCount);
    });

    [Fact]
    public void PaletteRefreshUpdatesBrushWithoutReplacingVisualTree() => AvaloniaTestHost.Run(() =>
    {
        PaletteRefreshSettingsWindow window = new();
        Border root = Assert.IsType<Border>(window.Content);
        TextBlock page = window.StablePage;
        SolidColorBrush background = Assert.IsType<SolidColorBrush>(root.Background);
        SolidColorBrush foreground = Assert.IsType<SolidColorBrush>(page.Foreground);

        window.SetColors(Colors.DarkRed, Colors.Yellow);

        Assert.Same(root, window.Content);
        Assert.Same(page, window.StablePage);
        Assert.Same(background, root.Background);
        Assert.Same(foreground, page.Foreground);
        Assert.Equal(Colors.DarkRed, background.Color);
        Assert.Equal(Colors.Yellow, foreground.Color);
    });

    [Fact]
    public void SettingsSearchStitchesFiltersAndRestoresPages() => AvaloniaTestHost.Run(() =>
    {
        SearchSettingsWindow window = new();
        window.Show();
        Border shellRoot = Assert.IsType<Border>(window.Content);
        SettingsSearchBox searchBox = Assert.Single(
            shellRoot.GetVisualDescendants().OfType<SettingsSearchBox>());
        TextBox searchInput = Assert.Single(searchBox.GetVisualDescendants().OfType<TextBox>());

        searchInput.Focus();
        window.KeyTextInput("al");

        Assert.True(window.AlphaPageRoot.IsVisible);
        Assert.True(window.AlphaCard.IsVisible);
        Assert.False(window.BetaPageRoot.IsVisible);
        Assert.False(window.BetaCard.IsVisible);
        Assert.Equal(1, window.PageCleanupCount);

        searchBox.Clear();

        Assert.Equal(SearchPage.Alpha, window.SelectedPage);
        Assert.True(window.AlphaPageRoot.IsVisible);
        Assert.Equal(3, window.PageCleanupCount);
        window.Close();
    });

    [Fact]
    public void SettingsSearchUsesLocalizedSynonymGroups() => AvaloniaTestHost.Run(() =>
    {
        SearchSettingsWindow window = new();
        window.Show();
        Border shellRoot = Assert.IsType<Border>(window.Content);
        SettingsSearchBox searchBox = Assert.Single(
            shellRoot.GetVisualDescendants().OfType<SettingsSearchBox>());

        searchBox.SearchText = "highest";

        Assert.True(window.AlphaPageRoot.IsVisible);
        Assert.True(window.MaximumCard.IsVisible);
        Assert.False(window.AlphaCard.IsVisible);
        Assert.False(window.BetaPageRoot.IsVisible);
        window.Close();
    });

    [Fact]
    public void SwatchColorUpdateReusesBrush() => AvaloniaTestHost.Run(() =>
    {
        SettingsSwatch swatch = new(CreatePalette(Colors.Black, Colors.White));
        swatch.SetColor(Colors.Blue, Colors.Black);
        SolidColorBrush brush = Assert.IsType<SolidColorBrush>(swatch.Background);

        swatch.SetColor(Colors.Red, Colors.Black);

        Assert.Same(brush, swatch.Background);
        Assert.Equal(Colors.Red, brush.Color);
    });

    private enum TestPage
    {
        Stable,
        Failing
    }

    private enum SearchPage
    {
        Alpha,
        Beta
    }

    private sealed class TestSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private static readonly SettingsPalette TestPalette = CreatePalette(Colors.Black, Colors.White);

        public TestSettingsWindow()
        {
            StablePage = new TextBlock { Text = "stable", DataContext = new object() };
            ConfigureSettingsWindow("Test", null);
            InitializeSettingsShell();
        }

        public TextBlock StablePage { get; }

        public TestPage SelectedPage => CurrentPageKey;

        public int FailedPageCleanupCount { get; private set; }

        protected override SettingsPalette ResolvePalette() => TestPalette;
        protected override bool EnableRoundedCorners => false;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;

        public void SelectFailingPage() => SelectPage(TestPage.Failing);

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new SettingsPageDescriptor<TestPage>(TestPage.Stable, "Stable", () => StablePage),
            new SettingsPageDescriptor<TestPage>(TestPage.Failing, "Failing", BuildFailingPage)
        ];

        protected override void Save()
        {
        }

        private Control BuildFailingPage()
        {
            AddPageCleanup(() => FailedPageCleanupCount++);
            throw new InvalidOperationException("expected page failure");
        }
    }

    private sealed class PaletteRefreshSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private Color _background = Colors.Black;
        private Color _foreground = Colors.White;

        public PaletteRefreshSettingsWindow()
        {
            StablePage = TrayAppDotNETSettingsUI.Text("stable", Palette);
            InitializeSettingsShell();
        }

        public TextBlock StablePage { get; }

        protected override bool EnableRoundedCorners => false;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;

        public void SetColors(Color background, Color foreground)
        {
            _background = background;
            _foreground = foreground;
            RefreshPalette();
        }

        protected override SettingsPalette ResolvePalette() => CreatePalette(_background, _foreground);

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new SettingsPageDescriptor<TestPage>(TestPage.Stable, "Stable", () => StablePage)
        ];

        protected override void Save()
        {
        }
    }

    private sealed class ResponsiveSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private static readonly SettingsPalette TestPalette = CreatePalette(Colors.Black, Colors.White);
        private readonly bool _enableResponsiveSidebarCollapse;

        public ResponsiveSettingsWindow(bool enableResponsiveSidebarCollapse, double width)
        {
            _enableResponsiveSidebarCollapse = enableResponsiveSidebarCollapse;
            ConfigureSettingsWindow("Responsive Test", null);
            MinWidth = 0;
            Width = width;
            ClientSize = new Size(width, 600);
            InitializeSettingsShell();
        }

        public double ConfiguredSidebarWidth => SidebarWidth;

        protected override bool EnableRoundedCorners => false;
        protected override bool EnableResponsiveSidebarCollapse => _enableResponsiveSidebarCollapse;
        protected override double SidebarCollapseThreshold => 750;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Responsive Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;
        protected override SettingsPalette ResolvePalette() => TestPalette;

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new SettingsPageDescriptor<TestPage>(TestPage.Stable, "Stable", static () => new TextBlock())
        ];

        protected override void Save()
        {
        }
    }

    private sealed class SidebarResizeSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private static readonly SettingsPalette TestPalette = CreatePalette(Colors.Black, Colors.White);
        private readonly TestSidebarWidthSettings _settings = new();

        public SidebarResizeSettingsWindow()
        {
            ConfigureSettingsWindow("Sidebar Resize Test", null);
            InitializeSettingsShell();
        }

        public double PersistedSidebarWidth => _settings.SettingsSidebarWidth;
        public double DisplayedSidebarWidth => FindBody().ColumnDefinitions[0].Width.Value;
        public int SaveCount { get; private set; }

        protected override bool EnableRoundedCorners => false;
        protected override ISettingsSidebarWidthSettings SidebarWidthSettings => _settings;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;
        protected override SettingsPalette ResolvePalette() => TestPalette;

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new SettingsPageDescriptor<TestPage>(TestPage.Stable, "Stable", static () => new TextBlock())
        ];

        protected override void Save() => SaveCount++;

        private Grid FindBody()
        {
            Border root = Assert.IsType<Border>(Content);
            Border contentSurface = Assert.IsType<Border>(root.Child);
            Grid shell = Assert.IsType<Grid>(contentSurface.Child);
            return Assert.Single(shell.Children.OfType<Grid>(), child => Grid.GetRow(child) == 1);
        }
    }

    private sealed class TestSidebarWidthSettings : ISettingsSidebarWidthSettings
    {
        public double SettingsSidebarWidth { get; set; }
    }

    private sealed class SearchSettingsWindow : SettingsWindowCommon<SearchPage>
    {
        private static readonly SettingsPalette TestPalette = CreatePalette(Colors.Black, Colors.White);

        public SearchSettingsWindow()
        {
            ConfigureSettingsWindow("Search Test", null);
            InitializeSettingsShell();
        }

        public StackPanel AlphaPageRoot { get; private set; } = null!;
        public StackPanel BetaPageRoot { get; private set; } = null!;
        public Border AlphaCard { get; private set; } = null!;
        public Border BetaCard { get; private set; } = null!;
        public Border MaximumCard { get; private set; } = null!;
        public int PageCleanupCount { get; private set; }
        public SearchPage SelectedPage => CurrentPageKey;

        protected override bool EnableRoundedCorners => false;
        protected override SearchPage DefaultPageKey => SearchPage.Alpha;
        protected override string HeaderText => "Search Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;
        protected override SettingsPalette ResolvePalette() => TestPalette;

        protected override IReadOnlyList<SettingsPageDescriptor<SearchPage>> CreatePageDescriptors() =>
        [
            new SettingsPageDescriptor<SearchPage>(SearchPage.Alpha, "Alpha", BuildAlphaPage),
            new SettingsPageDescriptor<SearchPage>(SearchPage.Beta, "Beta", BuildBetaPage)
        ];

        protected override void Save()
        {
        }

        private StackPanel BuildAlphaPage()
        {
            AddPageCleanup(() => PageCleanupCount++);
            AlphaPageRoot = PageStack("Alpha", Palette);
            AlphaCard = Card("Alpha option", "Starts the alpha feature.", null, Palette);
            MaximumCard = Card("Max apps per row", "Limits each row.", null, Palette);
            AlphaPageRoot.Children.Add(AlphaCard);
            AlphaPageRoot.Children.Add(MaximumCard);
            return AlphaPageRoot;
        }

        private StackPanel BuildBetaPage()
        {
            AddPageCleanup(() => PageCleanupCount++);
            BetaPageRoot = PageStack("Beta", Palette);
            BetaCard = Card("Beta option", "Starts the beta feature.", null, Palette);
            BetaPageRoot.Children.Add(BetaCard);
            return BetaPageRoot;
        }
    }

    private static SettingsPalette CreatePalette(Color background, Color foreground) =>
        new(
            background,
            foreground,
            Colors.Gray,
            Colors.DarkGray,
            Colors.DimGray,
            Colors.Black,
            Colors.DarkGray,
            Colors.LightGray,
            Colors.Gray,
            Colors.Blue,
            Colors.Blue,
            Colors.White,
            Colors.DarkBlue,
            Colors.Blue,
            Colors.DarkBlue,
            Colors.Blue,
            Colors.Gray,
            Colors.White,
            Colors.Red,
            Colors.DarkRed,
            Colors.White);

}
