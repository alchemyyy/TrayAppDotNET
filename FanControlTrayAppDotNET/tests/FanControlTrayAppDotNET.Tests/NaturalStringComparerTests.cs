using FanControlTrayAppDotNET.UI;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class NaturalStringComparerTests
{
    /// <summary>
    /// Verifies embedded digit runs sort by numeric value.
    /// </summary>
    [Fact]
    public void SortsEmbeddedNumbersByNumericValue()
    {
        List<string> labels =
        [
            "CCD11",
            "CCD8",
            "CCD10",
            "CCD6",
            "CCD9",
            "CCD7",
        ];

        List<string> ordered =
        [
            .. labels.OrderBy(static label => label, NaturalStringComparer.OrdinalIgnoreCase)
        ];

        Assert.Equal(["CCD6", "CCD7", "CCD8", "CCD9", "CCD10", "CCD11"], ordered);
    }

    /// <summary>
    /// Verifies natural ordering works across text prefixes and case differences.
    /// </summary>
    [Fact]
    public void SortsTextOrdinalIgnoreCaseBeforeNumericRuns()
    {
        List<string> labels =
        [
            "Core 10",
            "core 2",
            "Core 1",
            "Core 11",
        ];

        List<string> ordered =
        [
            .. labels.OrderBy(static label => label, NaturalStringComparer.OrdinalIgnoreCase)
        ];

        Assert.Equal(["Core 1", "core 2", "Core 10", "Core 11"], ordered);
    }
}
