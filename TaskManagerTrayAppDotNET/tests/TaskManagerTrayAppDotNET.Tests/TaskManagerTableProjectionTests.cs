using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerTableProjectionTests
{
    [Fact]
    public void BuildSortsGroupsAndTheirChildrenWithoutBreakingHierarchy()
    {
        TaskManagerTableRow[] rows =
        [
            Row("user-b", null, "Beta", 20, isGroup: true),
            Row("process-b2", "user-b", "Zulu", 5),
            Row("process-b1", "user-b", "Alpha", 10),
            Row("user-a", null, "Alpha", 30, isGroup: true),
            Row("process-a1", "user-a", "Gamma", 7)
        ];

        List<TaskManagerTableRow> projected = TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 0,
            sortDescending: false,
            collapsedGroupKeys: new HashSet<string>(),
            filterText: null);

        Assert.Equal(
            ["user-a", "process-a1", "user-b", "process-b1", "process-b2"],
            projected.Select(static row => row.Key));
    }

    [Fact]
    public void BuildFiltersChildrenButKeepsTheirParentVisible()
    {
        TaskManagerTableRow[] rows =
        [
            Row("user-a", null, "Alchemy", 30, isGroup: true),
            Row("terminal", "user-a", "Terminal", 7),
            Row("browser", "user-a", "Browser", 12)
        ];

        List<TaskManagerTableRow> projected = TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 0,
            sortDescending: false,
            collapsedGroupKeys: new HashSet<string>(),
            filterText: "term");

        Assert.Equal(["user-a", "terminal"], projected.Select(static row => row.Key));
    }

    [Fact]
    public void BuildKeepsAllChildrenWhenTheirParentMatchesFilter()
    {
        TaskManagerTableRow[] rows =
        [
            Row("user-a", null, "Alchemy", 30, isGroup: true),
            Row("terminal", "user-a", "Terminal", 7),
            Row("browser", "user-a", "Browser", 12)
        ];

        List<TaskManagerTableRow> projected = TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 0,
            sortDescending: false,
            collapsedGroupKeys: new HashSet<string>(),
            filterText: "alchemy");

        Assert.Equal(
            ["user-a", "browser", "terminal"],
            projected.Select(static row => row.Key));
    }

    [Fact]
    public void BuildHidesChildrenOfCollapsedGroups()
    {
        TaskManagerTableRow[] rows =
        [
            Row("user-a", null, "Alchemy", 30, isGroup: true),
            Row("terminal", "user-a", "Terminal", 7)
        ];
        HashSet<string> collapsed = new(StringComparer.Ordinal) { "user-a" };

        List<TaskManagerTableRow> projected = TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 1,
            sortDescending: true,
            collapsedGroupKeys: collapsed,
            filterText: null);

        Assert.Equal(["user-a"], projected.Select(static row => row.Key));
    }

    [Fact]
    public void BuildUsesTypedNumericValuesInsteadOfDisplayText()
    {
        TaskManagerTableRow[] rows =
        [
            Row("ten", null, "Ten", 10),
            Row("two", null, "Two", 2)
        ];

        List<TaskManagerTableRow> projected = TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 1,
            sortDescending: false,
            collapsedGroupKeys: new HashSet<string>(),
            filterText: null);

        Assert.Equal(["two", "ten"], projected.Select(static row => row.Key));
    }

    [Fact]
    public void BuildRejectsDuplicateStableKeys()
    {
        TaskManagerTableRow[] rows =
        [
            Row("same", null, "One", 1),
            Row("same", null, "Two", 2)
        ];

        Assert.Throws<ArgumentException>(() => TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 0,
            sortDescending: false,
            collapsedGroupKeys: new HashSet<string>(),
            filterText: null));
    }

    private static TaskManagerTableRow Row(
        string key,
        string? parentKey,
        string name,
        long value,
        bool isGroup = false) =>
        new()
        {
            Key = key,
            ParentKey = parentKey,
            IsGroup = isGroup,
            Cells =
            [
                TaskManagerTableCell.TextCell(name),
                TaskManagerTableCell.SignedCell(value.ToString(), value)
            ]
        };
}
