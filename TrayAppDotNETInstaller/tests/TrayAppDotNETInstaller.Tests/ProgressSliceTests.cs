using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class ProgressSliceTests
{
    [Fact]
    public void ForIndex_SplitsEvenlyAndEndsAtHundred()
    {
        Assert.Equal(new ProgressSlice(0, 33), ProgressSlice.ForIndex(0, 3));
        Assert.Equal(new ProgressSlice(33, 66), ProgressSlice.ForIndex(1, 3));
        Assert.Equal(new ProgressSlice(66, 100), ProgressSlice.ForIndex(2, 3));
        Assert.Equal(new ProgressSlice(0, 100), ProgressSlice.ForIndex(0, 1));
    }

    [Fact]
    public void ForIndex_ClampsDegenerateInputs()
    {
        Assert.Equal(ProgressSlice.Full, ProgressSlice.ForIndex(0, 0));
        Assert.Equal(new ProgressSlice(50, 100), ProgressSlice.ForIndex(5, 2));
        Assert.Equal(new ProgressSlice(0, 50), ProgressSlice.ForIndex(-1, 2));
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(50, 50)]
    [InlineData(100, 60)]
    [InlineData(-10, 40)]
    [InlineData(200, 60)]
    public void Map_RescalesAndClampsIntoSlice(int innerPercent, int expected)
    {
        ProgressSlice slice = new(40, 60);

        Assert.Equal(expected, slice.Map(innerPercent));
    }

    [Fact]
    public void Portion_ProducesSubSlice()
    {
        ProgressSlice slice = new(0, 50);

        Assert.Equal(new ProgressSlice(0, 20), slice.Portion(0, 40));
        Assert.Equal(new ProgressSlice(20, 50), slice.Portion(40, 100));
    }

    [Fact]
    public void Portion_ThenMap_ComposesLikeInstallSlices()
    {
        // Second of two apps: extraction is the first 40 percent of its half, install the rest
        ProgressSlice appSlice = ProgressSlice.ForIndex(1, 2);
        ProgressSlice extract = appSlice.Portion(0, 40);
        ProgressSlice install = appSlice.Portion(40, 100);

        Assert.Equal(50, extract.Map(0));
        Assert.Equal(70, extract.Map(100));
        Assert.Equal(70, install.Map(0));
        Assert.Equal(85, install.Map(50));
        Assert.Equal(100, install.Map(100));
    }
}
