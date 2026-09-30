using Avalonia;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class ProcessColumnPropertiesWindowTestCollection
{
    public const string CollectionName = "Process column properties window tests";
}

[Collection(ProcessColumnPropertiesWindowTestCollection.CollectionName)]
public sealed class ProcessColumnPropertiesWindowTests
{
    [Fact]
    public async Task StatusWindowPublishesDisplayModeAndGlyphCentering()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                ProcessColumnSetting setting = ProcessColumnSettings.CreateDefault()
                    .Single(static candidate => candidate.Column == ProcessTableColumnKind.Status);
                setting.CenterStatusGlyphs = false;
                List<ProcessColumnSetting> published = [];
                using ProcessColumnPropertiesWindow window = ProcessColumnPropertiesWindow.Create(
                    setting,
                    CreatePalette(),
                    enableRoundedCorners: false,
                    published.Add);

                Assert.IsType<StatusProcessColumnPropertiesWindow>(window);
                SettingsComboBox comboBox = window.GetLogicalDescendants().OfType<SettingsComboBox>().Single();
                SettingsToggle centerGlyphsToggle =
                    window.GetLogicalDescendants().OfType<SettingsToggle>().Single();
                Assert.Equal(ProcessStatusDisplayMode.Glyph, comboBox.SelectedItem?.Tag);
                Assert.True(centerGlyphsToggle.IsEnabled);
                Assert.False(centerGlyphsToggle.IsChecked);
                Assert.Empty(published);

                centerGlyphsToggle.IsChecked = true;

                ProcessColumnSetting centered = Assert.Single(published);
                Assert.True(centered.CenterStatusGlyphs);

                comboBox.SelectedItem = comboBox.Items.Single(static item =>
                    item.Tag is ProcessStatusDisplayMode.Text);

                Assert.Equal(expected: 2, published.Count);
                ProcessColumnSetting textMode = published[^1];
                Assert.Equal(ProcessTableColumnKind.Status, textMode.Column);
                Assert.Equal(ProcessStatusDisplayMode.Text, textMode.StatusDisplayMode);
                Assert.True(textMode.CenterStatusGlyphs);

                // Centering only applies to glyphs, so text mode dims the toggle
                Assert.False(centerGlyphsToggle.IsEnabled);
            },
            CancellationToken.None);
    }

    private static SettingsPalette CreatePalette() => new(
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

    private sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    private static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
