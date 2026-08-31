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
            Row(key: "user-b", parentKey: null, name: "Beta", value: 20, isGroup: true),
            Row(key: "process-b2", parentKey: "user-b", name: "Zulu", value: 5),
            Row(key: "process-b1", parentKey: "user-b", name: "Alpha", value: 10),
            Row(key: "user-a", parentKey: null, name: "Alpha", value: 30, isGroup: true),
            Row(key: "process-a1", parentKey: "user-a", name: "Gamma", value: 7)
        ];

        List<TaskManagerTableRow> projected = TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 0,
            sortDescending: false,
            new HashSet<string>(),
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
            Row(key: "user-a", parentKey: null, name: "Alchemy", value: 30, isGroup: true),
            Row(key: "terminal", parentKey: "user-a", name: "Terminal", value: 7),
            Row(key: "browser", parentKey: "user-a", name: "Browser", value: 12)
        ];

        List<TaskManagerTableRow> projected = TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 0,
            sortDescending: false,
            new HashSet<string>(),
            filterText: "term");

        Assert.Equal(["user-a", "terminal"], projected.Select(static row => row.Key));
    }

    [Fact]
    public void BuildKeepsAllChildrenWhenTheirParentMatchesFilter()
    {
        TaskManagerTableRow[] rows =
        [
            Row(key: "user-a", parentKey: null, name: "Alchemy", value: 30, isGroup: true),
            Row(key: "terminal", parentKey: "user-a", name: "Terminal", value: 7),
            Row(key: "browser", parentKey: "user-a", name: "Browser", value: 12)
        ];

        List<TaskManagerTableRow> projected = TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 0,
            sortDescending: false,
            new HashSet<string>(),
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
            Row(key: "user-a", parentKey: null, name: "Alchemy", value: 30, isGroup: true),
            Row(key: "terminal", parentKey: "user-a", name: "Terminal", value: 7)
        ];
        HashSet<string> collapsed = new(StringComparer.Ordinal) { "user-a" };

        List<TaskManagerTableRow> projected = TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 1,
            sortDescending: true,
            collapsed,
            filterText: null);

        Assert.Equal(["user-a"], projected.Select(static row => row.Key));
    }

    [Fact]
    public void BuildUsesTypedNumericValuesInsteadOfDisplayText()
    {
        TaskManagerTableRow[] rows =
        [
            Row(key: "ten", parentKey: null, name: "Ten", value: 10),
            Row(key: "two", parentKey: null, name: "Two", value: 2)
        ];

        List<TaskManagerTableRow> projected = TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 1,
            sortDescending: false,
            new HashSet<string>(),
            filterText: null);

        Assert.Equal(["two", "ten"], projected.Select(static row => row.Key));
    }

    [Fact]
    public void BuildRejectsDuplicateStableKeys()
    {
        TaskManagerTableRow[] rows =
        [
            Row(key: "same", parentKey: null, name: "One", value: 1),
            Row(key: "same", parentKey: null, name: "Two", value: 2)
        ];

        Assert.Throws<ArgumentException>(() => TaskManagerTableProjection.Build(
            rows,
            columnCount: 2,
            sortColumnIndex: 0,
            sortDescending: false,
            new HashSet<string>(),
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
