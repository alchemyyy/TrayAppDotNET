using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessSearchQueryTests
{
    [Fact]
    public void PlainTextUsesCaseInsensitiveNameOrPIDContainsSearch()
    {
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.Name, "CHROME.EXE");
        row.SetNumeric(ProcessTableColumnKind.ProcessID, "4242", 4_242);

        ProcessSearchQuery nameQuery = Parse("rome");
        ProcessSearchQuery processIDQuery = Parse("242");
        ProcessSearchQuery literalRegexCharacters = Parse("[a-z]");

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
        row.SetText(ProcessTableColumnKind.Status, "Running");
        row.SetNumeric(ProcessTableColumnKind.PrivateMemory, "120 MB", 120 * 1_048_576);
        row.SetNumeric(ProcessTableColumnKind.WorkingSet, "200 MB", 200 * 1_048_576);
        ProcessSearchQuery query = Parse(
            " {Status} = \"Running\" && ( {PrivateMemory} > 100m || {WorkingSet} < 100m ) ");

        Assert.True(query.IsValid, query.ErrorMessage);
        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void AndHasHigherPrecedenceThanOr()
    {
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.Status, "Suspended");
        row.SetNumeric(ProcessTableColumnKind.ProcessID, "5", 5);
        row.SetNumeric(ProcessTableColumnKind.CPU, "20.0%", 20);
        ProcessSearchQuery query = Parse("{Status}=Running||{PID}=5&&{CPU}>10%");

        Assert.True(query.Matches(0, row.Resolve));

        row.SetNumeric(ProcessTableColumnKind.CPU, "5.0%", 5);
        Assert.False(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void LifetimeUnitsSelectProcessesBetweenOneAndTwoHours()
    {
        ProcessSearchQuery query = Parse("{Lifetime}>=1h&&{Lifetime}<2h");
        SearchRow row = new();

        row.SetNumeric(
            ProcessTableColumnKind.Lifetime,
            "1:30:00",
            TimeSpan.FromMinutes(90).Ticks);
        Assert.True(query.Matches(0, row.Resolve));

        row.SetNumeric(
            ProcessTableColumnKind.Lifetime,
            "2:00:00",
            TimeSpan.FromHours(2).Ticks);
        Assert.False(query.Matches(0, row.Resolve));
        Assert.True(query.RequiresAllProcessSamples);
    }

    [Fact]
    public void DisplayedLifetimeClockCanBeUsedAsANumericOperand()
    {
        ProcessSearchQuery query = Parse("{Lifetime}=\"1d 8:10:22\"");
        SearchRow row = new();
        row.SetNumeric(
            ProcessTableColumnKind.Lifetime,
            "1d 8:10:22",
            TimeSpan.FromDays(1).Ticks
            + TimeSpan.FromHours(8).Ticks
            + TimeSpan.FromMinutes(10).Ticks
            + TimeSpan.FromSeconds(22).Ticks);

        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void MemorySuffixesUseBinaryByteUnits()
    {
        ProcessSearchQuery query = Parse("{Working set (memory)}>=100k&&{Working set (memory)}<2m");
        SearchRow row = new();
        row.SetNumeric(ProcessTableColumnKind.WorkingSet, "1,024 K", 1_048_576);

        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void DiskRateExpressionsUseBinaryMegabytesPerSecond()
    {
        ProcessSearchQuery query = Parse("{Disk}>=1 MB/s&&{Disk}<2 MB/s");
        SearchRow row = new();
        row.SetNumeric(ProcessTableColumnKind.Disk, "1.5 MB/s", 1.5 * 1_048_576);

        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void NetworkRateExpressionsUseDecimalMegabitsPerSecond()
    {
        ProcessSearchQuery query = Parse("{Network}>=10 Mbps&&{Network}<20 Mbps");
        SearchRow row = new();
        row.SetNumeric(ProcessTableColumnKind.Network, "12.0 Mbps", 12_000_000);

        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void RegexOperatorsAreExplicitAndCaseInsensitive()
    {
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.Name, "CHROME.EXE");
        row.SetText(ProcessTableColumnKind.CommandLine, "chrome.exe --type=renderer --profile=Default");
        ProcessSearchQuery nameQuery = Parse("{Name}=~\"^(chrome|firefox)\\.exe$\"");
        ProcessSearchQuery commandLineQuery = Parse(
            "{Command line}=~\"--type=(renderer|gpu-process)(?: |$)\"");
        ProcessSearchQuery negativeQuery = Parse("{Command line}!~\"--type=utility\"");

        Assert.True(nameQuery.Matches(0, row.Resolve));
        Assert.True(commandLineQuery.Matches(0, row.Resolve));
        Assert.True(negativeQuery.Matches(0, row.Resolve));
    }

    [Fact]
    public void BareComponentsInsideExpressionsRetainDefaultSearchBehavior()
    {
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.Name, "chrome.exe");
        row.SetNumeric(ProcessTableColumnKind.ProcessID, "8080", 8_080);
        row.SetText(ProcessTableColumnKind.Status, "Running");

        Assert.True(Parse("chrome&&{Status}=Running").Matches(0, row.Resolve));
        Assert.True(Parse("808||{Status}=Suspended").Matches(0, row.Resolve));
    }

    [Fact]
    public void ColumnReferencesResolveCustomNicknames()
    {
        List<ProcessColumnSetting> settings = ProcessColumnSettings.CreateDefault();
        ProcessColumnSetting commandLine = settings.Single(
            static setting => setting.Column == ProcessTableColumnKind.CommandLine);
        commandLine.Nickname = "Arguments";
        ProcessSearchQuery query = ProcessSearchQuery.Parse(
            "{Arguments}=~\"--safe-mode\"",
            settings);
        SearchRow row = new();
        row.SetText(ProcessTableColumnKind.CommandLine, "browser.exe --safe-mode");

        Assert.True(query.Matches(0, row.Resolve));
    }

    [Fact]
    public void QueryCollectsEveryReferencedColumn()
    {
        ProcessSearchQuery query = Parse(
            "{Status}=Running&&({Lifetime}<2h||{Command line}=~\"service\")");
        ulong expectedMask = ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Status)
                             | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Lifetime)
                             | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.CommandLine);

        Assert.Equal(expectedMask, query.RequiredColumnMask);
        Assert.True(query.RequiresAllProcessSamples);
    }

    [Theory]
    [InlineData("{Name}=~\"[\"")]
    [InlineData("{Started at}=anything")]
    [InlineData("{Lifetime}>one fortnight")]
    [InlineData("{Status}=Running&&")]
    [InlineData("({Status}=Running")]
    public void InvalidExpressionsDoNotThrowDuringMatching(string expression)
    {
        ProcessSearchQuery query = Parse(expression);
        SearchRow row = new();

        Assert.False(query.IsValid);
        Assert.NotNull(query.ErrorMessage);
        Assert.False(query.Matches(0, row.Resolve));
        Assert.Equal(0UL, query.RequiredColumnMask);
    }

    private static ProcessSearchQuery Parse(string expression) =>
        ProcessSearchQuery.Parse(expression, ProcessColumnSettings.CreateDefault());

    private sealed class SearchRow
    {
        private readonly Dictionary<ProcessTableColumnKind, ProcessSearchColumnValue> _values = [];

        public void SetText(ProcessTableColumnKind column, string text) =>
            _values[column] = ProcessSearchColumnValue.TextOnly(text);

        public void SetNumeric(ProcessTableColumnKind column, string text, double value) =>
            _values[column] = ProcessSearchColumnValue.Numeric(text, value);

        public ProcessSearchColumnValue Resolve(int rowIndex, ProcessTableColumnKind column) =>
            _values.TryGetValue(column, out ProcessSearchColumnValue value)
                ? value
                : ProcessSearchColumnValue.TextOnly(string.Empty);
    }
}
