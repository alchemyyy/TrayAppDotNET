namespace TaskManagerTrayAppDotNET.UI.ProcessesPage;

/// <summary>Describes the Processes columns to the shared search; column IDs are <see cref="ProcessTableColumnKind"/> values.</summary>
internal static class ProcessSearchColumns
{
    private static readonly int[] DefaultColumnIDs =
        [(int)ProcessTableColumnKind.Name, (int)ProcessTableColumnKind.ProcessID];

    /// <summary>Builds the catalog for the current column settings, which supply the nicknames.</summary>
    public static SearchColumnCatalog CreateCatalog(IReadOnlyList<ProcessColumnSetting> columnSettings)
    {
        ArgumentNullException.ThrowIfNull(columnSettings);

        string[] nicknames = new string[ProcessTableColumnCatalog.Definitions.Length];
        for (int settingIndex = 0; settingIndex < columnSettings.Count; settingIndex++)
        {
            ProcessColumnSetting setting = columnSettings[settingIndex];
            if (!Enum.IsDefined(setting.Column) || string.IsNullOrWhiteSpace(setting.Nickname)) continue;
            nicknames[(int)setting.Column] = setting.Nickname.Trim();
        }

        SearchColumn[] columns = new SearchColumn[ProcessTableColumnCatalog.Definitions.Length];
        for (int definitionIndex = 0; definitionIndex < columns.Length; definitionIndex++)
        {
            ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Definitions[definitionIndex];
            columns[definitionIndex] = new SearchColumn(
                (int)definition.Kind,
                definition.Title,
                definition.Kind.ToString(),
                GetValueKind(definition.Kind))
            {
                Nickname = nicknames[definitionIndex] ?? string.Empty,
                RequiredMask = ProcessTableColumnCatalog.GetMask(definition.Kind)
            };
        }

        return new SearchColumnCatalog(columns, DefaultColumnIDs);
    }

    /// <summary>Parses a Processes search against the current column settings.</summary>
    public static SearchQuery Parse(string? filterText, IReadOnlyList<ProcessColumnSetting> columnSettings) =>
        SearchQuery.Parse(filterText, CreateCatalog(columnSettings));

    /// <summary>Whether a query reads a column that changes over time, so every process must be sampled.</summary>
    public static bool RequiresAllProcessSamples(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return !query.IsEmpty
               && query.IsValid
               && (query.RequiredColumnMask & ProcessTableColumnCatalog.DynamicMask) != 0;
    }

    private static SearchValueKind GetValueKind(ProcessTableColumnKind column) => column switch
    {
        ProcessTableColumnKind.Name
            or ProcessTableColumnKind.Status
            or ProcessTableColumnKind.UserName
            or ProcessTableColumnKind.ImagePath
            or ProcessTableColumnKind.CommandLine
            or ProcessTableColumnKind.OperatingSystemContext
            or ProcessTableColumnKind.Platform
            or ProcessTableColumnKind.Elevated
            or ProcessTableColumnKind.UACVirtualization
            or ProcessTableColumnKind.Description
            or ProcessTableColumnKind.DataExecutionPrevention
            or ProcessTableColumnKind.IOPriority
            or ProcessTableColumnKind.PackageName
            or ProcessTableColumnKind.EnterpriseContext
            or ProcessTableColumnKind.PowerThrottling
            or ProcessTableColumnKind.GPUEngine
            or ProcessTableColumnKind.DPIAwareness
            or ProcessTableColumnKind.Architecture
            or ProcessTableColumnKind.HardwareStackProtection
            or ProcessTableColumnKind.ExtendedControlFlowGuard
            or ProcessTableColumnKind.Isolation
            or ProcessTableColumnKind.NPUEngine => SearchValueKind.Text,
        ProcessTableColumnKind.CPUTime or ProcessTableColumnKind.Lifetime => SearchValueKind.Duration,
        ProcessTableColumnKind.CPU
            or ProcessTableColumnKind.CPUSingle
            or ProcessTableColumnKind.GPU
            or ProcessTableColumnKind.NPU
            or ProcessTableColumnKind.CPUUtility => SearchValueKind.Percentage,
        ProcessTableColumnKind.Disk
            or ProcessTableColumnKind.DiskRead
            or ProcessTableColumnKind.DiskWrite => SearchValueKind.ByteRate,
        ProcessTableColumnKind.Network => SearchValueKind.BitRate,
        ProcessTableColumnKind.IOReadBytes
            or ProcessTableColumnKind.IOWriteBytes
            or ProcessTableColumnKind.IOOtherBytes => SearchValueKind.Bytes,
        _ when ProcessColumnSettings.IsMemoryColumn(column) => SearchValueKind.Bytes,
        _ => SearchValueKind.Number
    };
}
