using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using BrightnessTrayAppDotNET.Localization;
using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.UI.Settings;
using BrightnessTrayAppDotNET.Visuals;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class SettingsNavigationTests
{
    [Fact]
    public void Windows11SettingsNavigationIsDisabledByDefault()
    {
        AppSettings settings = new();

        Assert.False(settings.UseWindows11SettingsNavigation);
    }

    [Fact]
    public void Windows11SettingsNavigationRoundTripsThroughSettingsFile()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), $"btadn-navigation-{Guid.NewGuid():N}.xml");
        try
        {
            AppSettings settings = new() { UseWindows11SettingsNavigation = true };
            settings.Save(settingsPath);

            AppSettings loaded = AppSettings.LoadOrDefault(settingsPath);

            Assert.True(loaded.UseWindows11SettingsNavigation);
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [Fact]
    public void SettingsSidebarWidthRoundTripsThroughSettingsFile()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), $"btadn-sidebar-{Guid.NewGuid():N}.xml");
        try
        {
            AppSettings settings = new() { SettingsSidebarWidth = 312.5 };
            settings.Save(settingsPath);

            AppSettings loaded = AppSettings.LoadOrDefault(settingsPath);

            Assert.Equal(312.5, loaded.SettingsSidebarWidth);
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [Fact]
    public void Windows11SettingsNavigationBuildsAllBrightnessPageIcons() => AvaloniaTestHost.Run(() =>
    {
        LocalizationManager.Instance.Initialize(Strings.ResourceManager);
        AppSettings settings = new() { UseWindows11SettingsNavigation = true };
        settings.OnTrayXmlDeserialized();
        BrightnessSettingsWindow window = new(settings);
        try
        {
            window.Show();
            window.UpdateLayout();

            SettingsNavItem[] navigationItems = window.GetVisualDescendants()
                .OfType<SettingsNavItem>()
                .ToArray();

            Assert.Equal(8, navigationItems.Length);
            AssertNavigationGlyph(navigationItems[0], "\uE71D");
            AssertNavigationGlyph(navigationItems[1], "\uE8A7");
            AssertNavigationGlyph(navigationItems[2], "\uE957");
            AssertNavigationGlyph(navigationItems[3], "\uE7F4");
            AssertNavigationGlyph(navigationItems[4], "\uE92E");
            EnvironmentalCurveGlyphIcon environmentalIcon = Assert.Single(
                navigationItems[5].GetVisualDescendants().OfType<EnvironmentalCurveGlyphIcon>());
            Assert.Equal(21, environmentalIcon.Width);
            Assert.Equal(21, environmentalIcon.Height);
            AssertNavigationGlyph(navigationItems[6], "\uE790");
            AssertNavigationGlyph(navigationItems[7], "\uE946");

            Border selectedIndicator = FindIndicator(navigationItems[0]);
            SolidColorBrush indicatorBrush = Assert.IsType<SolidColorBrush>(selectedIndicator.Background);
            Assert.Equal(Color.Parse("#A6A5A1"), indicatorBrush.Color);

            window.SelectPage(BrightnessSettingsPage.Theme);
            window.UpdateLayout();
            TextBlock settingTitle = Assert.Single(
                window.GetVisualDescendants().OfType<TextBlock>(),
                static textBlock => textBlock.Text == CommonStrings.Settings_Theme_Windows11Navigation_Title);
            Border settingsCard = settingTitle.GetVisualAncestors()
                .OfType<Border>()
                .First(static border => border.GetVisualDescendants().OfType<SettingsToggle>().Any());
            SettingsToggle navigationToggle = Assert.Single(
                settingsCard.GetVisualDescendants().OfType<SettingsToggle>());
            Assert.True(navigationToggle.IsChecked);
        }
        finally
        {
            window.Close();
        }
    });

    private static void AssertNavigationGlyph(SettingsNavItem navigationItem, string expectedGlyph)
    {
        Assert.Single(
            navigationItem.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == expectedGlyph);
    }

    private static Border FindIndicator(SettingsNavItem navigationItem) =>
        Assert.Single(
            navigationItem.GetVisualDescendants().OfType<Border>(),
            static border => border.Width == 3 && border.Height == 16);
}
