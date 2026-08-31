using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class AppThemeDefaultsTests
{
    [Fact]
    public void EverySharedThemeColorHasAnAxamlDefault()
    {
        AppThemeResources resources = new();
        string[] propertyNames =
        [
            .. typeof(AppTheme).GetProperties()
                .Where(static property => property.DeclaringType == typeof(AppTheme)
                                          && property.PropertyType == typeof(ThemeColor))
                .Select(static property => property.Name)
        ];

        Assert.NotEmpty(propertyNames);
        foreach (string propertyName in propertyNames)
            Assert.IsType<ThemeColor>(resources[$"AppTheme.{propertyName}"]);

        Assert.Equal(
            resources.SingleColor(nameof(AppTheme.TextSelectionHighlightAlpha)).A,
            AppTheme.TextSelectionHighlightAlpha);
    }

#if DEBUG
    [Fact]
    public void ThemeColorReloadPreservesExistingResourceReferences()
    {
        ThemeColor existingColor = new(lightHex: "112233", darkHex: "445566");
        ResourceDictionary currentResources = new()
        {
            ["AppTheme.Existing"] = existingColor, ["AppTheme.Removed"] = new ThemeColor("000000")
        };
        ResourceDictionary candidateResources = new()
        {
            ["AppTheme.Existing"] = new ThemeColor(lightHex: "AABBCC", darkHex: "DDEEFF"),
            ["AppTheme.Added"] = new ThemeColor("123456")
        };

        AppThemeResourceReader.SynchronizeColors(currentResources, candidateResources);

        Assert.Same(existingColor, currentResources["AppTheme.Existing"]);
        Assert.Equal(expected: "#AABBCC", existingColor.LightHex);
        Assert.Equal(expected: "#DDEEFF", existingColor.DarkHex);
        Assert.True(currentResources.ContainsKey("AppTheme.Removed"));
        Assert.IsType<ThemeColor>(currentResources["AppTheme.Added"]);
    }

    [Fact]
    public void ColorReloadReplacesSingleColorResources()
    {
        ResourceDictionary currentResources = new() { ["VolumeAppTheme.MeterPeakColorDefault"] = Colors.White };
        ResourceDictionary candidateResources = new() { ["VolumeAppTheme.MeterPeakColorDefault"] = Colors.Red };

        AppThemeResourceReader.SynchronizeColors(currentResources, candidateResources);

        Assert.Equal(Colors.Red, currentResources["VolumeAppTheme.MeterPeakColorDefault"]);
    }

    [Fact]
    public void AxamlReloadUpdatesExistingThemeColorsAndNotifiesConsumers() => AvaloniaTestHost.Run(() =>
    {
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            path2: "TrayAppDotNET.ThemeHotReloadTests",
            Guid.NewGuid().ToString("N"));
        string sourcePath = Path.Combine(temporaryDirectory, path2: "AppTheme.axaml");
        string callerFilePath = Path.Combine(temporaryDirectory, path2: "AppThemeCatalog.cs");
        AppThemeHotReloadStore<AppThemeResources> store =
            AppThemeHotReloadStore<AppThemeResources>.Create(
                catalogName: "Test",
                static () => new AppThemeResources(),
                callerFilePath: callerFilePath);
        ThemeColor existingBackground = store.Current.Color(nameof(AppTheme.Background));
        int notificationCount = 0;
        Action onResourcesReloaded = () => notificationCount++;

        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            sourcePath,
            contents: """
                      <ResourceDictionary
                          x:Class="TrayAppDotNETCommon.Visuals.AppThemeResources"
                          xmlns="https://github.com/avaloniaui"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                          xmlns:visuals="clr-namespace:TrayAppDotNETCommon.Visuals">
                          <visuals:ThemeColor
                              x:Key="AppTheme.Background"
                              LightHex="#123456"
                              DarkHex="#ABCDEF" />
                      </ResourceDictionary>
                      """);

        AppThemeHotReload.ResourcesReloaded += onResourcesReloaded;
        try
        {
            store.ReloadNow();

            Assert.Same(existingBackground, store.Current.Color(nameof(AppTheme.Background)));
            Assert.Equal(expected: "#123456", existingBackground.LightHex);
            Assert.Equal(expected: "#ABCDEF", existingBackground.DarkHex);
            Assert.Equal(expected: 1, notificationCount);
        }
        finally
        {
            AppThemeHotReload.ResourcesReloaded -= onResourcesReloaded;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    });
#endif

    [Fact]
    public void BackgroundUsesNeutralLightAndDarkDefaults()
    {
        using AppTheme theme = new();

        Assert.Equal(expected: "#F3F3F3", theme.Background.LightHex);
        Assert.Equal(expected: "#202020", theme.Background.DarkHex);
    }

    [Fact]
    public void InteractiveColorsUseModeAppropriateNeutralDefaults()
    {
        using AppTheme theme = new();

        Assert.Equal(expected: "#222222", theme.Accent.LightHex);
        Assert.Equal(expected: "#DDDDDD", theme.Accent.DarkHex);
        Assert.Equal(expected: "#DFDFDF", theme.SearchListItemSelected.LightHex);
        Assert.Equal(expected: "#454545", theme.SearchListItemSelected.DarkHex);
        Assert.Equal(expected: "#E9E9E9", theme.SearchListItemHover.LightHex);
        Assert.Equal(expected: "#292929", theme.SearchListItemHover.DarkHex);
    }

    [Fact]
    public void StructuralFallbacksRemainAchromatic()
    {
        using AppTheme theme = new();
        (string Name, ThemeColor Color)[] structuralColors =
        [
            (nameof(theme.Background), theme.Background),
            (nameof(theme.Foreground), theme.Foreground),
            (nameof(theme.Border), theme.Border),
            (nameof(theme.Separator), theme.Separator),
            (nameof(theme.Hover), theme.Hover),
            (nameof(theme.HoverDeep), theme.HoverDeep),
            (nameof(theme.Pressed), theme.Pressed),
            (nameof(theme.PressedDeep), theme.PressedDeep),
            (nameof(theme.ControlBackground), theme.ControlBackground),
            (nameof(theme.ControlBackgroundDeep), theme.ControlBackgroundDeep),
            (nameof(theme.ControlBorder), theme.ControlBorder),
            (nameof(theme.DisabledForeground), theme.DisabledForeground),
            (nameof(theme.Accent), theme.Accent),
            (nameof(theme.Acrylic), theme.Acrylic),
            (nameof(theme.SecondaryForeground), theme.SecondaryForeground),
            (nameof(theme.FooterBackground), theme.FooterBackground),
            (nameof(theme.SliderTrack), theme.SliderTrack),
            (nameof(theme.SliderProgress), theme.SliderProgress),
            (nameof(theme.SliderThumb), theme.SliderThumb),
            (nameof(theme.ButtonHover), theme.ButtonHover),
            (nameof(theme.ButtonPressed), theme.ButtonPressed),
            (nameof(theme.IconForeground), theme.IconForeground),
            (nameof(theme.CardBackground), theme.CardBackground),
            (nameof(theme.TextBoxFocused), theme.TextBoxFocused),
            (nameof(theme.SearchListItemSelected), theme.SearchListItemSelected),
            (nameof(theme.SearchListItemHover), theme.SearchListItemHover),
            (nameof(theme.ToggleSwitchOnTrack), theme.ToggleSwitchOnTrack),
            (nameof(theme.ToggleSwitchOnThumb), theme.ToggleSwitchOnThumb),
            (nameof(theme.CloseButtonGlyphActive), theme.CloseButtonGlyphActive),
            (nameof(theme.FlyoutOverlayBackdrop), theme.FlyoutOverlayBackdrop),
            (nameof(theme.FlyoutShadow), theme.FlyoutShadow),
            (nameof(theme.MenuShadow), theme.MenuShadow)
        ];

        foreach ((string name, ThemeColor color) in structuralColors)
        {
            AssertAchromatic(name, variant: "light", color.Light);
            AssertAchromatic(name, variant: "dark", color.Dark);
        }
    }

    private static void AssertAchromatic(string name, string variant, Color color)
    {
        Assert.True(
            color.R == color.G && color.G == color.B,
            $"{name} {variant} fallback must remain neutral, but was {color}.");
    }
}
