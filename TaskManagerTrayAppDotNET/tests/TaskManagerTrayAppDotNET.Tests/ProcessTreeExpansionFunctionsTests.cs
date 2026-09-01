using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTreeExpansionFunctionsTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, true)]
    [InlineData(2, 0, true)]
    [InlineData(3, 1, true)]
    [InlineData(3, 2, false)]
    [InlineData(4, 0, false)]
    public void IsDescendantOrSelfFollowsParentIndexes(
        int candidateRowIndex,
        int rootRowIndex,
        bool expected)
    {
        int[] parentRowIndexes = [-1, 0, 1, 1, -1];

        Assert.Equal(
            expected,
            ProcessTreeExpansionFunctions.IsDescendantOrSelf(
                parentRowIndexes,
                candidateRowIndex,
                rootRowIndex));
    }

    [Fact]
    public void IsDescendantOrSelfStopsAtMalformedCycle()
    {
        int[] parentRowIndexes = [-1, 2, 1];

        Assert.False(ProcessTreeExpansionFunctions.IsDescendantOrSelf(
            parentRowIndexes,
            candidateRowIndex: 1,
            rootRowIndex: 0));
    }
}
