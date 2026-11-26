using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using TaskManagerTrayAppDotNET.UI.ProcessesPage;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessSearchQueryTests
{
    [Fact]
    public void PlainTextUsesCaseInsensitiveNameOrPIDContainsSearch()
    {
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.Name, text: "CHROME.EXE");
        row.SetNumeric(ProcessTableColumnKind.ProcessID, text: "4242", value: 4_242);

        SearchQuery nameQuery = Parse("rome");
        SearchQuery processIDQuery = Parse("242");
        SearchQuery literalRegexCharacters = Parse("[a-z]");

        Assert.True(nameQuery.Matches(0, row.Resolve));
        Assert.True(processIDQuery.Matches(0, row.Resolve));
        Assert.False(literalRegexCharacters.Matches(0, row.Resolve));
        Assert.Equal(
            ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Name)
            | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.ProcessID),
            nameQuery.RequiredColumnMask);
    }

    [Fact]
    public void BooleanExpressionSupportsWhitespaceParenthesesAndColumnAliases()
    {
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.Status, text: "Running");
        row.SetNumeric(ProcessTableColumnKind.PrivateMemory, text: "120 MB", 120 * 1_048_576);
        row.SetNumeric(ProcessTableColumnKind.WorkingSet, text: "200 MB", 200 * 1_048_576);
        SearchQuery query = Parse(
            " {Status} = \"Running\" && ( {PrivateMemory} > 100m || {WorkingSet} < 100m ) ");

        Assert.True(query.IsValid, query.ErrorMessage);
        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void AndHasHigherPrecedenceThanOr()
    {
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.Status, text: "Suspended");
        row.SetNumeric(ProcessTableColumnKind.ProcessID, text: "5", value: 5);
        row.SetNumeric(ProcessTableColumnKind.CPU, text: "20.0%", value: 20);
        SearchQuery query = Parse("{Status}=Running||{PID}=5&&{CPU}>10%");

        Assert.True(query.Matches(0, row.Resolve));

        row.SetNumeric(ProcessTableColumnKind.CPU, text: "5.0%", value: 5);
        Assert.False(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void CPUSingleSupportsPercentageExpressions()
    {
        SearchQuery query = Parse("{CPU (single)}>=75%");
        SearchRow row = new();
        row.SetNumeric(ProcessTableColumnKind.CPUSingle, text: "80.0%", value: 80);

        Assert.True(query.IsValid, query.ErrorMessage);
        Assert.True(query.Matches(0, row.Resolve));
        Assert.Equal(
            ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.CPUSingle),
            query.RequiredColumnMask);
    }

    [Fact]
    public void LifetimeUnitsSelectProcessesBetweenOneAndTwoHours()
    {
        SearchQuery query = Parse("{Lifetime}>=1h&&{Lifetime}<2h");
        SearchRow row = new();

        row.SetNumeric(
            ProcessTableColumnKind.Lifetime,
            text: "1:30:00",
            TimeSpan.FromMinutes(90).Ticks);
        Assert.True(query.Matches(0, row.Resolve));

        row.SetNumeric(
            ProcessTableColumnKind.Lifetime,
            text: "2:00:00",
            TimeSpan.FromHours(2).Ticks);
        Assert.False(query.Matches(0, row.Resolve));
        Assert.True(ProcessSearchColumns.RequiresAllProcessSamples(query));
    }

    [Fact]
    public void DisplayedLifetimeClockCanBeUsedAsANumericOperand()
    {
        SearchQuery query = Parse("{Lifetime}=\"1d 8:10:22\"");
        SearchRow row = new();
        row.SetNumeric(
            ProcessTableColumnKind.Lifetime,
            text: "1d 8:10:22",
            TimeSpan.FromDays(1).Ticks
            + TimeSpan.FromHours(8).Ticks
            + TimeSpan.FromMinutes(10).Ticks
            + TimeSpan.FromSeconds(22).Ticks);

        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void MemorySuffixesUseBinaryByteUnits()
    {
        SearchQuery query = Parse("{Working set (memory)}>=100k&&{Working set (memory)}<2m");
        SearchRow row = new();
        row.SetNumeric(ProcessTableColumnKind.WorkingSet, text: "1,024 K", value: 1_048_576);

        Assert.True(query.Matches(0, row.Resolve));
    }

    [Theory]
    [InlineData("Disk", ProcessTableColumnKind.Disk)]
    [InlineData("Disk read", ProcessTableColumnKind.DiskRead)]
    [InlineData("Disk write", ProcessTableColumnKind.DiskWrite)]
    public void DiskRateExpressionsUseBinaryMegabytesPerSecond(string columnName, ProcessTableColumnKind column)
    {
        SearchQuery query = Parse($"{{{columnName}}}>=1 MB/s&&{{{columnName}}}<2 MB/s");
        SearchRow row = new();
        // 1.95 MiB/s is above 2,000,000 bytes per second, so only binary megabytes keep it in range
        row.SetNumeric(column, text: "2.0 MB/s", 1.95 * 1_048_576);

        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void NetworkRateExpressionsUseDecimalMegabitsPerSecond()
    {
        SearchQuery query = Parse("{Network}>=10 Mbps&&{Network}<20 Mbps");
        SearchRow row = new();
        row.SetNumeric(ProcessTableColumnKind.Network, text: "12.0 Mbps", value: 12_000_000);

        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void RegexOperatorsAreExplicitAndCaseInsensitive()
    {
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.Name, text: "CHROME.EXE");
        row.SetText(ProcessTableColumnKind.CommandLine, text: "chrome.exe --type=renderer --profile=Default");
        SearchQuery nameQuery = Parse("{Name}=~\"^(chrome|firefox)\\.exe$\"");
        SearchQuery commandLineQuery = Parse(
            "{Command line}=~\"--type=(renderer|gpu-process)(?: |$)\"");
        SearchQuery negativeQuery = Parse("{Command line}!~\"--type=utility\"");

        Assert.True(nameQuery.Matches(0, row.Resolve));
        Assert.True(commandLineQuery.Matches(0, row.Resolve));
        Assert.True(negativeQuery.Matches(0, row.Resolve));
    }

    [Fact]
    public void BareComponentsInsideExpressionsRetainDefaultSearchBehavior()
    {
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.Name, text: "chrome.exe");
        row.SetNumeric(ProcessTableColumnKind.ProcessID, text: "8080", value: 8_080);
        row.SetText(ProcessTableColumnKind.Status, text: "Running");

        Assert.True(Parse("chrome&&{Status}=Running").Matches(0, row.Resolve));
        Assert.True(Parse("808||{Status}=Suspended").Matches(0, row.Resolve));
    }

    [Fact]
    public void StatusExpressionsMatchStatusTextAndProcessesWithoutStatus()
    {
        SearchRow efficiencyRow = new();
        efficiencyRow.SetText(ProcessTableColumnKind.Status, ProcessStatusFunctions.EfficiencyModeText);
        SearchRow noStatusRow = new();
        noStatusRow.SetText(ProcessTableColumnKind.Status, text: string.Empty);
        SearchQuery efficiencyQuery = Parse("{Status}=\"Efficiency mode\"");
        SearchQuery noStatusQuery = Parse("{Status}=\"\"");

        Assert.True(efficiencyQuery.IsValid, efficiencyQuery.ErrorMessage);
        Assert.True(noStatusQuery.IsValid, noStatusQuery.ErrorMessage);
        Assert.True(efficiencyQuery.Matches(0, efficiencyRow.Resolve));
        Assert.False(efficiencyQuery.Matches(0, noStatusRow.Resolve));
        Assert.True(noStatusQuery.Matches(0, noStatusRow.Resolve));
        Assert.False(noStatusQuery.Matches(0, efficiencyRow.Resolve));
    }

    [Fact]
    public void ColumnReferencesResolveCustomNicknames()
    {
        List<ProcessColumnSetting> settings = ProcessColumnSettings.CreateDefault();
        ProcessColumnSetting commandLine =
            settings.Single(static setting => setting.Column == ProcessTableColumnKind.CommandLine);
        commandLine.Nickname = "Arguments";
        SearchQuery query = ProcessSearchColumns.Parse(
            filterText: "{Arguments}=~\"--safe-mode\"",
            settings);
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.CommandLine, text: "browser.exe --safe-mode");

        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void QueryCollectsEveryReferencedColumn()
    {
        SearchQuery query = Parse(
            "{Status}=Running&&({Lifetime}<2h||{Command line}=~\"service\")");
        ulong expectedMask = ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Status)
                             | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Lifetime)
                             | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.CommandLine);

        Assert.Equal(expectedMask, query.RequiredColumnMask);
        Assert.True(ProcessSearchColumns.RequiresAllProcessSamples(query));
    }

    [Theory]
    [InlineData("{Name}=~\"[\"")]
    [InlineData("{Started at}=anything")]
    [InlineData("{Lifetime}>one fortnight")]
    [InlineData("{Status}=Running&&")]
    [InlineData("({Status}=Running")]
    public void InvalidExpressionsDoNotThrowDuringMatching(string expression)
    {
        SearchQuery query = Parse(expression);
        SearchRow row = new();

        Assert.False(query.IsValid);
        Assert.NotNull(query.ErrorMessage);
        Assert.False(query.Matches(0, row.Resolve));
        Assert.Equal(expected: 0UL, query.RequiredColumnMask);
    }

    private static SearchQuery Parse(string expression) =>
        ProcessSearchColumns.Parse(expression, ProcessColumnSettings.CreateDefault());

    private sealed class SearchRow
    {
        private readonly Dictionary<ProcessTableColumnKind, SearchColumnValue> _values = [];

        public void SetText(ProcessTableColumnKind column, string text) =>
            _values[column] = SearchColumnValue.TextOnly(text);

        public void SetNumeric(ProcessTableColumnKind column, string text, double value) =>
            _values[column] = SearchColumnValue.Numeric(text, value);

        public SearchColumnValue Resolve(int rowIndex, int column) =>
            _values.TryGetValue((ProcessTableColumnKind)column, out SearchColumnValue value)
                ? value
                : SearchColumnValue.TextOnly(string.Empty);
    }
}
