using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>
/// The title bar draws its icon from the frame nearest its size on the current monitor. These pin the choice,
/// since a wrong pick is only visible as a blurry icon at one particular display scaling.
/// </summary>
public sealed class InstallerIconsTests
{
    // The frames every generated app.ico carries, shuffled to prove nothing relies on their order
    private static readonly int[] GeneratedWidths = [256, 16, 48, 20, 128, 32, 24, 96, 64, 40];

    // The title bar icon is 16 units, so each case is 16 at one display scaling
    [Theory]
    [InlineData(16, 16)]
    [InlineData(20, 20)]
    [InlineData(24, 24)]
    [InlineData(32, 32)]
    [InlineData(40, 40)]
    [InlineData(48, 48)]
    public void SelectFrameIndex_PrefersAnExactMatch(int targetWidth, int expectedWidth)
    {
        int index = InstallerIcons.SelectFrameIndex(GeneratedWidths, targetWidth);

        Assert.Equal(expectedWidth, GeneratedWidths[index]);
    }

    // 175, 225 and 350 percent fall between frames
    [Theory]
    [InlineData(28, 32)]
    [InlineData(36, 40)]
    [InlineData(56, 64)]
    [InlineData(129, 256)]
    public void SelectFrameIndex_ScalesDownFromTheNextLargerFrame(int targetWidth, int expectedWidth)
    {
        int index = InstallerIcons.SelectFrameIndex(GeneratedWidths, targetWidth);

        Assert.Equal(expectedWidth, GeneratedWidths[index]);
    }

    [Fact]
    public void SelectFrameIndex_FallsBackToTheWidestWhenEveryFrameIsTooSmall()
    {
        int[] widths = [16, 32, 24];

        int index = InstallerIcons.SelectFrameIndex(widths, targetPixelWidth: 64);

        Assert.Equal(32, widths[index]);
    }

    [Fact]
    public void SelectFrameIndex_IsMinusOneWithoutFrames()
    {
        Assert.Equal(-1, InstallerIcons.SelectFrameIndex([], targetPixelWidth: 16));
    }

    [Fact]
    public void SelectFrameIndex_TakesTheFirstOfEqualWidths()
    {
        // An icon can carry the same size twice at different colour depths
        int[] widths = [32, 16, 16];

        Assert.Equal(1, InstallerIcons.SelectFrameIndex(widths, targetPixelWidth: 16));
    }
}
