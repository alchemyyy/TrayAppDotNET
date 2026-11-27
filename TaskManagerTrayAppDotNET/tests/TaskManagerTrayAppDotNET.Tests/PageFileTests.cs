using System.Globalization;
using Avalonia.Media;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI.Performance;
using static TaskManagerTrayAppDotNET.Tests.TestLocalization;
using AppStrings = TaskManagerTrayAppDotNET.Localization.Strings;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PageFileTests
{
    private const ulong Gibibyte = 1_073_741_824;

    [Theory]
    [InlineData(@"H:\pagefile.sys", "H")]
    [InlineData(@"c:\pagefile.sys", "C")]
    [InlineData(@"\Device\HarddiskVolume3\pagefile.sys", @"\Device\HarddiskVolume3\pagefile.sys")]
    public void DriveNameIsTheLetterOrTheWholePath(string path, string expected) =>
        Assert.Equal(expected, PageFileReader.GetDriveName(path));

    [Fact]
    public void LegendShowsDriveUseAndSizeInGigabytes()
    {
        PageFileSnapshot pageFile = new(
            @"H:\pagefile.sys",
            "H",
            InUseBytes: (ulong)(7.9 * Gibibyte),
            SizeBytes: (ulong)(40.2 * Gibibyte),
            PeakInUseBytes: 0);

        Assert.Equal(
            F(
                nameof(AppStrings.Perf_PageFile_LegendFormat),
                "H",
                7.9.ToString("0.0", CultureInfo.CurrentCulture),
                40.2.ToString("0.0", CultureInfo.CurrentCulture)),
            PageFileHistoryView.FormatLegend(pageFile));
    }

    [Fact]
    public void LaterPageFilesGetLighterShadesOfTheAccentUpToTheLimit()
    {
        Color accent = Color.FromRgb(0x58, 0x83, 0xD0);

        Color first = PageFileHistoryView.GetShade(accent, index: 0, step: 0.3, maximumLightening: 0.75);
        Color second = PageFileHistoryView.GetShade(accent, index: 1, step: 0.3, maximumLightening: 0.75);
        Color capped = PageFileHistoryView.GetShade(accent, index: 9, step: 0.3, maximumLightening: 0.75);

        Assert.Equal(accent, first);
        Assert.True(second.R > first.R && second.G > first.G && second.B > first.B);
        Assert.Equal(PageFileHistoryView.GetShade(accent, index: 3, step: 0.3, maximumLightening: 0.75), capped);
        Assert.True(capped.R < byte.MaxValue);
    }
}
