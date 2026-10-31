using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerReorderListLogicTests
{
    [Theory]
    [InlineData(-1, 2, 5, -1)]  // moved up into the middle: keep the Move up button
    [InlineData(-1, 0, 5, 1)]   // reached the top: Move up is disabled, use Move down
    [InlineData(1, 2, 5, 1)]    // moved down into the middle: keep the Move down button
    [InlineData(1, 4, 5, -1)]   // reached the bottom: Move down is disabled, use Move up
    public void KeyboardFocusStaysOnAnEnabledMoveButtonOfTheMovedItem(
        int direction,
        int newVisibleIndex,
        int visibleCount,
        int expected) =>
        Assert.Equal(
            expected,
            TaskManagerReorderListLogic.FocusDirectionAfterMove(direction, newVisibleIndex, visibleCount));

    [Fact]
    public void FilterItemsPreservesCurrentOrderInsteadOfScoreRanking()
    {
        ReorderItem prefix = new("alphabet soup");
        ReorderItem exact = new("alpha");
        ReorderItem unmatched = new("beta");
        List<ReorderItem> items = [prefix, exact, unmatched];

        List<ReorderItem> filtered = TaskManagerReorderListLogic.FilterItems(
            items,
            filter: "alpha",
            static item => item.Name);

        Assert.Equal(expected: 2, filtered.Count);
        Assert.Same(prefix, filtered[0]);
        Assert.Same(exact, filtered[1]);
    }

    [Fact]
    public void FilterItemsCombinesTextAndItemPredicates()
    {
        ReorderItem includedMatch = new(name: "alpha", isIncluded: true);
        ReorderItem excludedMatch = new(name: "alphabet", isIncluded: false);
        ReorderItem includedMismatch = new(name: "beta", isIncluded: true);
        List<ReorderItem> items = [includedMatch, excludedMatch, includedMismatch];

        List<ReorderItem> filtered = TaskManagerReorderListLogic.FilterItems(
            items,
            filter: "alpha",
            static item => item.Name,
            static item => item.IsIncluded);

        Assert.Single(filtered);
        Assert.Same(includedMatch, filtered[0]);
    }

    [Fact]
    public void MoveVisibleItemPreservesEveryUnmatchedSlot()
    {
        ReorderItem first = new("First");
        ReorderItem unmatchedOne = new("Unmatched one");
        ReorderItem second = new("Second");
        ReorderItem unmatchedTwo = new("Unmatched two");
        ReorderItem third = new("Third");
        List<ReorderItem> items = [first, unmatchedOne, second, unmatchedTwo, third];
        List<ReorderItem> visibleItems = [first, second, third];

        bool changed = TaskManagerReorderListLogic.MoveVisibleItem(
            items,
            visibleItems,
            second,
            targetVisibleIndex: 0);

        Assert.True(changed);
        Assert.Same(second, items[0]);
        Assert.Same(unmatchedOne, items[1]);
        Assert.Same(first, items[2]);
        Assert.Same(unmatchedTwo, items[3]);
        Assert.Same(third, items[4]);
    }

    [Fact]
    public void MoveVisibleItemClampsToLastMatchingSlot()
    {
        ReorderItem first = new("First");
        ReorderItem unmatched = new("Unmatched");
        ReorderItem second = new("Second");
        ReorderItem third = new("Third");
        List<ReorderItem> items = [first, unmatched, second, third];
        List<ReorderItem> visibleItems = [first, second, third];

        bool changed = TaskManagerReorderListLogic.MoveVisibleItem(
            items,
            visibleItems,
            first,
            int.MaxValue);

        Assert.True(changed);
        Assert.Same(second, items[0]);
        Assert.Same(unmatched, items[1]);
        Assert.Same(third, items[2]);
        Assert.Same(first, items[3]);
    }

    [Theory]
    [InlineData(1, 0, 3, 0)]
    [InlineData(1, 1, 3, 1)]
    [InlineData(1, 2, 3, 1)]
    [InlineData(1, 3, 3, 2)]
    [InlineData(0, 0, 3, 0)]
    [InlineData(0, 3, 3, 2)]
    [InlineData(2, 0, 3, 0)]
    [InlineData(2, 3, 3, 2)]
    [InlineData(0, int.MaxValue, 3, 2)]
    [InlineData(2, int.MinValue, 3, 0)]
    public void ResolveDropTargetIndexAccountsForSourceRemoval(
        int sourceVisibleIndex,
        int insertionIndex,
        int visibleCount,
        int expectedTargetIndex)
    {
        int targetIndex = TaskManagerReorderListLogic.ResolveDropTargetIndex(
            sourceVisibleIndex,
            insertionIndex,
            visibleCount);

        Assert.Equal(expectedTargetIndex, targetIndex);
    }

    private sealed class ReorderItem(string name, bool isIncluded = true)
    {
        public string Name { get; } = name;
        public bool IsIncluded { get; } = isIncluded;
    }
}
