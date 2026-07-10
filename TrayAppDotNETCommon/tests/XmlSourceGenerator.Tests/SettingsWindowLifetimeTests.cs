using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SettingsWindowLifetimeTests
{
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

    private enum TestPage
    {
        Stable,
        Failing
    }

    private sealed class TestSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private static readonly SettingsPalette TestPalette = new(
            Colors.Black,
            Colors.White,
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

        public TestSettingsWindow()
        {
            StablePage = new TextBlock { Text = "stable", DataContext = new object() };
            InitializeSettingsShell();
        }

        public TextBlock StablePage { get; }

        public TestPage SelectedPage => CurrentPageKey;

        public int FailedPageCleanupCount { get; private set; }

        protected override SettingsPalette Palette => TestPalette;
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
}
