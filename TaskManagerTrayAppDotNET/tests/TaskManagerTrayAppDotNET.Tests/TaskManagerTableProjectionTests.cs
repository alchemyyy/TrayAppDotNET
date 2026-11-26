using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerTableProjectionTests
{
    private const double ColumnWidth = 100;

    private static readonly TaskManagerTableSchema NameValueSchema = new(
    [
        new TaskManagerTableColumn(Key: "name", Title: "Name", ColumnWidth),
        new TaskManagerTableColumn(Key: "value", Title: "Value", ColumnWidth, SearchValueKind: SearchValueKind.Number)
    ], minimumColumnWidth: 1);

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

        List<TaskManagerTableRow> projected = Build(rows);

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

        List<TaskManagerTableRow> projected = Build(rows, filterText: "term");

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

        List<TaskManagerTableRow> projected = Build(rows, filterText: "alchemy");

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

        List<TaskManagerTableRow> projected = Build(
            rows,
            sortColumnIndex: 1,
            sortDescending: true,
            collapsedGroupKeys: collapsed);

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

        List<TaskManagerTableRow> projected = Build(rows, sortColumnIndex: 1);

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

        Assert.Throws<ArgumentException>(() => Build(rows));
    }

    [Fact]
    public void PlainTextMatchesAnyColumnIncludingNumbers()
    {
        TaskManagerTableRow[] rows =
        [
            Row(key: "spooler", parentKey: null, name: "Spooler", value: 4242),
            Row(key: "audio", parentKey: null, name: "Audiosrv", value: 17)
        ];

        Assert.Equal(["spooler"], Build(rows, filterText: "242").Select(static row => row.Key));
        Assert.Equal(["audio"], Build(rows, filterText: "AUDIO").Select(static row => row.Key));
    }

    [Fact]
    public void ColumnExpressionsCombineRegexNumbersAndBooleanOperators()
    {
        TaskManagerTableRow[] rows =
        [
            Row(key: "terminal", parentKey: null, name: "Terminal", value: 7),
            Row(key: "browser", parentKey: null, name: "Browser", value: 12),
            Row(key: "editor", parentKey: null, name: "Editor", value: 30)
        ];

        Assert.Equal(
            ["browser", "terminal"],
            Build(rows, filterText: "{Name}=~\"^(term|brow)\"").Select(static row => row.Key));
        Assert.Equal(
            ["browser", "editor"],
            Build(rows, filterText: "{Value}>10").Select(static row => row.Key));
        Assert.Equal(
            ["editor", "terminal"],
            Build(rows, filterText: "{name}!~\"brow\"&&({Value}<10||{Value}>=30)")
                .Select(static row => row.Key));
    }

    [Theory]
    [InlineData("{Missing}=1")]
    [InlineData("{Value}>lots")]
    [InlineData("{Name}=~\"[\"")]
    public void InvalidExpressionsShowNoRows(string filterText)
    {
        TaskManagerTableRow[] rows = [Row(key: "terminal", parentKey: null, name: "Terminal", value: 7)];

        Assert.Empty(Build(rows, filterText: filterText));
    }

    [Fact]
    public void ColumnSearchKindsReadTheirUnitsFromRawCellValues()
    {
        TaskManagerTableSchema schema = new(
        [
            new TaskManagerTableColumn(Key: "name", Title: "Name", ColumnWidth),
            new TaskManagerTableColumn(Key: "cpuTime", Title: "CPU time", ColumnWidth,
                SearchValueKind: SearchValueKind.Duration),
            new TaskManagerTableColumn(Key: "cpu", Title: "CPU", ColumnWidth,
                SearchValueKind: SearchValueKind.Percentage),
            new TaskManagerTableColumn(Key: "memory", Title: "Memory", ColumnWidth,
                SearchValueKind: SearchValueKind.Bytes),
            new TaskManagerTableColumn(Key: "disk", Title: "Disk", ColumnWidth,
                SearchValueKind: SearchValueKind.ByteRate),
            new TaskManagerTableColumn(Key: "network", Title: "Network", ColumnWidth,
                SearchValueKind: SearchValueKind.BitRate)
        ], minimumColumnWidth: 1);
        TaskManagerTableRow busy = new()
        {
            Key = "busy",
            Cells =
            [
                TaskManagerTableCell.TextCell("busy"),
                TaskManagerTableCell.SignedCell("1:30:00", TimeSpan.FromMinutes(90).Ticks),
                TaskManagerTableCell.DecimalCell("40%", 40),
                TaskManagerTableCell.SignedCell("1,536 MB", 1_536L * 1_048_576),
                // 1.95 MiB/s is above 2,000,000 bytes per second, so only binary megabytes keep it in range
                TaskManagerTableCell.DecimalCell("2 MB/s", 1.95 * 1_048_576),
                // 1.5 MB/s is 12 Mbps
                TaskManagerTableCell.DecimalCell("12 Mbps", 1_500_000)
            ]
        };
        TaskManagerTableRow idle = new()
        {
            Key = "idle",
            Cells =
            [
                TaskManagerTableCell.TextCell("idle"),
                TaskManagerTableCell.SignedCell("0:00:10", TimeSpan.FromSeconds(10).Ticks),
                TaskManagerTableCell.DecimalCell("0%", 0),
                TaskManagerTableCell.SignedCell("10 MB", 10L * 1_048_576),
                TaskManagerTableCell.Empty,
                TaskManagerTableCell.Empty
            ]
        };
        TaskManagerTableRow[] rows = [busy, idle];

        string[] expressions =
        [
            "{CPU time}>=1h&&{CPU time}<2h",
            "{CPU}>10%",
            "{Memory}>=1.5g",
            "{Disk}>=1 MB/s&&{Disk}<2 MB/s",
            "{Network}>=10 Mbps&&{Network}<20 Mbps"
        ];
        foreach (string expression in expressions)
            Assert.Equal(["busy"], Build(rows, schema, expression).Select(static row => row.Key));

        // Unavailable values never match a number comparison, not even !=
        Assert.Equal(["busy"], Build(rows, schema, "{Network}!=0").Select(static row => row.Key));
    }

    private static List<TaskManagerTableRow> Build(
        IReadOnlyList<TaskManagerTableRow> rows,
        string? filterText = null,
        int sortColumnIndex = 0,
        bool sortDescending = false,
        IReadOnlySet<string>? collapsedGroupKeys = null) =>
        Build(rows, NameValueSchema, filterText, sortColumnIndex, sortDescending, collapsedGroupKeys);

    private static List<TaskManagerTableRow> Build(
        IReadOnlyList<TaskManagerTableRow> rows,
        TaskManagerTableSchema schema,
        string? filterText,
        int sortColumnIndex = 0,
        bool sortDescending = false,
        IReadOnlySet<string>? collapsedGroupKeys = null) =>
        TaskManagerTableProjection.Build(
            rows,
            schema.Columns.Length,
            sortColumnIndex,
            sortDescending,
            collapsedGroupKeys ?? new HashSet<string>(),
            SearchQuery.Parse(filterText, schema.SearchCatalog),
            schema.ResolveSearchValue);

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
