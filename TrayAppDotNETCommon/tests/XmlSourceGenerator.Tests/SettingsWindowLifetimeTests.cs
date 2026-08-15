using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
