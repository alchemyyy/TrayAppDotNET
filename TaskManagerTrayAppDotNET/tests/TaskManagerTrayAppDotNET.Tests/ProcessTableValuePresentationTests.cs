using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTableValuePresentationTests
{
    [Fact]
    public void UsesCompactUnavailableText()
    {
        Assert.Equal(expected: "N/A", ProcessTableValuePresentation.UnavailableText);
    }

    [Fact]
    public void UnavailableSamplesSortBelowZero()
    {
        double[] unavailableValues =
        [
            -1,
            double.NaN,
            double.NegativeInfinity,
            double.PositiveInfinity
        ];

        foreach (double unavailableValue in unavailableValues)
        {
            Assert.True(ProcessTableValuePresentation.CompareNonnegativeDouble(unavailableValue, right: 0) < 0);
            Assert.True(ProcessTableValuePresentation.CompareNonnegativeDouble(left: 0, right: unavailableValue) > 0);
        }
    }

    [Fact]
    public void UnavailableSamplesShareOneSortRank()
    {
        Assert.Equal(
            expected: 0,
            ProcessTableValuePresentation.CompareNonnegativeDouble(
                double.PositiveInfinity,
                double.NaN));
    }

    [Fact]
    public void AvailableSamplesRetainNumericOrdering()
    {
        Assert.True(ProcessTableValuePresentation.CompareNonnegativeDouble(left: 0, right: 0.1) < 0);
        Assert.True(ProcessTableValuePresentation.CompareNonnegativeDouble(left: 1, right: 0) > 0);
    }
}
