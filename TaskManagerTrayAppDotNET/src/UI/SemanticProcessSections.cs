using System.Globalization;

namespace TaskManagerTrayAppDotNET.UI;

internal enum SemanticProcessSectionRowKind : byte
{
    None,
    Spacer,
    Header
}

/// <summary>Defines Task Manager-style semantic process section presentation.</summary>
internal static class SemanticProcessSections
{
    private const int AppSpacerProcessID = -1;
    private const int AppHeaderProcessID = -2;
    private const int BackgroundSpacerProcessID = -3;
    private const int BackgroundHeaderProcessID = -4;
    private const int WindowsSpacerProcessID = -5;
    private const int WindowsHeaderProcessID = -6;

    public const int Count = 3;
    public const int RowsPerSection = 2;
    public const int FirstGroupSyntheticProcessID = -7;

    public static bool IsEnabled(
        ProcessGroupingStyle groupingStyle,
        ProcessTableColumnKind sortColumn) =>
        groupingStyle == ProcessGroupingStyle.Semantic
        && sortColumn == ProcessTableColumnKind.Name;

    public static SemanticProcessGroupClassification GetClassification(int sectionIndex) =>
        sectionIndex switch
        {
            0 => SemanticProcessGroupClassification.App,
            1 => SemanticProcessGroupClassification.Background,
            2 => SemanticProcessGroupClassification.Windows,
            _ => throw new ArgumentOutOfRangeException(nameof(sectionIndex))
        };

    public static string GetTitle(
        SemanticProcessGroupClassification classification,
        int entryCount)
    {
        if (entryCount < 0) throw new ArgumentOutOfRangeException(nameof(entryCount));

        string label = classification switch
        {
            SemanticProcessGroupClassification.App => "Apps",
            SemanticProcessGroupClassification.Background => "Background processes",
            SemanticProcessGroupClassification.Windows => "Windows processes",
            _ => throw new ArgumentOutOfRangeException(nameof(classification))
        };
        return $"{label} ({entryCount.ToString(CultureInfo.CurrentCulture)})";
    }

    public static ProcessInstanceKey GetInstanceKey(
        SemanticProcessGroupClassification classification,
        SemanticProcessSectionRowKind rowKind)
    {
        int processID = (classification, rowKind) switch
        {
            (SemanticProcessGroupClassification.App, SemanticProcessSectionRowKind.Spacer) =>
                AppSpacerProcessID,
            (SemanticProcessGroupClassification.App, SemanticProcessSectionRowKind.Header) =>
                AppHeaderProcessID,
            (SemanticProcessGroupClassification.Background, SemanticProcessSectionRowKind.Spacer) =>
                BackgroundSpacerProcessID,
            (SemanticProcessGroupClassification.Background, SemanticProcessSectionRowKind.Header) =>
                BackgroundHeaderProcessID,
            (SemanticProcessGroupClassification.Windows, SemanticProcessSectionRowKind.Spacer) =>
                WindowsSpacerProcessID,
            (SemanticProcessGroupClassification.Windows, SemanticProcessSectionRowKind.Header) =>
                WindowsHeaderProcessID,
            _ => throw new ArgumentOutOfRangeException(nameof(rowKind))
        };
        return new ProcessInstanceKey(processID, CreationTimeTicks: 0);
    }
}
