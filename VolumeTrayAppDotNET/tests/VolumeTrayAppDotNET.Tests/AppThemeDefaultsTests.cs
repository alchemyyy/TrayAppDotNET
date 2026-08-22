using Avalonia.Media;
using VolumeTrayAppDotNET.Models;
using VolumeTrayAppDotNET.Visuals;
using Xunit;
using ThemeColor = TrayAppDotNETCommon.Visuals.ThemeColor;

namespace VolumeTrayAppDotNET.Tests;

public sealed class AppThemeDefaultsTests
{
    [Fact]
    public void MeterPeakDefaultsComeFromAxamlResources()
    {
        AppThemeResources resources = new();

        Assert.Equal(resources.Color(nameof(AppTheme.MeterPeakColorDefault)), AppTheme.MeterPeakColorDefault);
        Assert.Equal(
            resources.Color(nameof(AppTheme.MeterPeakStereoColorDefault)),
            AppTheme.MeterPeakStereoColorDefault);
        Assert.Equal("#FFFFFFFF", AppTheme.MeterPeakColorDefaultHex);
        Assert.Equal("#80FFFFFF", AppTheme.MeterPeakStereoColorDefaultHex);
        Assert.Equal(
            resources.Color("LegacyMeterPeakColorDefault"),
            Color.Parse(AppTheme.LegacyMeterPeakColorDefaultHex));
        Assert.Equal(
            resources.Color("LegacyMeterPeakStereoColorDefault"),
            Color.Parse(AppTheme.LegacyMeterPeakStereoColorDefaultHex));
    }

    [Fact]
    public void LegacyMeterPeakDefaultsRemainResourceBacked()
    {
        AppSettings settings = new()
        {
            MeterPeakColorHex = "#FFFFFFFF",
            MeterPeakStereoColorHex = "#80FFFFFF"
        };

        Assert.Equal(AppTheme.MeterPeakColorDefaultHex, settings.MeterPeakColorHex);
        Assert.Equal(AppTheme.MeterPeakStereoColorDefaultHex, settings.MeterPeakStereoColorHex);
        Assert.Equal(AppTheme.MeterPeakColorDefault, settings.EffectiveMeterPeakColor);
        Assert.Equal(AppTheme.MeterPeakStereoColorDefault, settings.EffectiveMeterPeakStereoColor);
    }

    [Fact]
    public void CustomMeterPeakColorsStillOverrideAxamlDefaults()
    {
        AppSettings settings = new()
        {
            MeterPeakColorHex = "#102030",
            MeterPeakStereoColorHex = "#80405060"
        };

        Assert.Equal(Color.Parse("#102030"), settings.EffectiveMeterPeakColor);
        Assert.Equal(Color.Parse("#80405060"), settings.EffectiveMeterPeakStereoColor);
    }

    [Fact]
    public void DeserializedOverridesDoNotMutateAxamlDefaults()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"VolumeTrayAppDotNET.AppThemeDefaultsTests.{Guid.NewGuid():N}.xml");
        using AppTheme source = new()
        {
            Background = new ThemeColor("123456", "ABCDEF")
        };

        try
        {
            source.Save(path);
            using AppTheme loaded = AppTheme.Load(path);
            using AppTheme untouched = new();

            Assert.Equal("#123456", loaded.Background.LightHex);
            Assert.Equal("#ABCDEF", loaded.Background.DarkHex);
            Assert.Equal("#F3F3F3", untouched.Background.LightHex);
            Assert.Equal("#202020", untouched.Background.DarkHex);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
