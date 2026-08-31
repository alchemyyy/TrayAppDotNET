using System.Diagnostics.CodeAnalysis;
using System.Xml.Serialization;
using TaskManagerTrayAppDotNET.UI;

namespace TaskManagerTrayAppDotNET.Models;

public enum ProcessMemoryUnit : byte
{
    Kilobytes,
    Megabytes,
    Gigabytes,
    PercentageOfSystem
}

public sealed class ProcessColumnSetting
{
    [XmlAttribute]
    public ProcessTableColumnKind Column { get; set; }

    [XmlAttribute]
    public bool Visible { get; set; }

    [XmlAttribute]
    public double Width { get; set; }

    [XmlAttribute]
    [AllowNull]
    public string Nickname
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    [XmlAttribute]
    public bool ShowPercentSuffix { get; set; } = true;

    [XmlAttribute]
    public bool ShowDecimalUsage { get; set; } = true;

    [XmlAttribute]
    public ProcessMemoryUnit MemoryUnit { get; set; } = ProcessMemoryUnit.Kilobytes;

    [XmlAttribute]
    [AllowNull]
    public string MemorySuffix
    {
        get;
        set => field = value ?? string.Empty;
    } = "K";

    [XmlAttribute]
    public bool ShowUserNamePrefix { get; set; }
}

internal static class ProcessColumnSettings
{
    public const double MinimumWidth = 40;

    public static List<ProcessColumnSetting> CreateDefault()
    {
        List<ProcessColumnSetting> settings = new(ProcessTableColumnCatalog.Definitions.Length);
        foreach (ProcessTableColumnDefinition definition in ProcessTableColumnCatalog.Definitions)
        {
            settings.Add(new ProcessColumnSetting
            {
                Column = definition.Kind, Visible = definition.DefaultVisible, Width = definition.DefaultWidth
            });
        }

        return settings;
    }

    public static List<ProcessColumnSetting> Normalize(IEnumerable<ProcessColumnSetting>? source)
    {
        List<ProcessColumnSetting> normalized = new(ProcessTableColumnCatalog.Definitions.Length);
        HashSet<ProcessTableColumnKind> used = [];
        if (source != null)
        {
            foreach (ProcessColumnSetting setting in source)
            {
                if (!Enum.IsDefined(setting.Column) || !used.Add(setting.Column)) continue;

                ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
                ProcessColumnSetting clone = Clone(setting);
                clone.Width = NormalizeWidth(setting.Width, definition.DefaultWidth);
                normalized.Add(clone);
            }
        }

        foreach (ProcessTableColumnDefinition definition in ProcessTableColumnCatalog.Definitions)
        {
            if (!used.Add(definition.Kind)) continue;

            normalized.Add(new ProcessColumnSetting
            {
                Column = definition.Kind, Visible = definition.DefaultVisible, Width = definition.DefaultWidth
            });
        }

        if (!normalized.Any(static setting => setting.Visible))
            normalized[0].Visible = true;
        return normalized;
    }

    /// <summary>Returns a normalized independent copy of one column setting.</summary>
    public static ProcessColumnSetting Clone(ProcessColumnSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        ProcessMemoryUnit memoryUnit = NormalizeMemoryUnit(setting.MemoryUnit);
        return new ProcessColumnSetting
        {
            Column = setting.Column,
            Visible = setting.Visible,
            Width = setting.Width,
            Nickname = setting.Nickname,
            ShowPercentSuffix = setting.ShowPercentSuffix,
            ShowDecimalUsage = setting.ShowDecimalUsage,
            MemoryUnit = memoryUnit,
            MemorySuffix = string.IsNullOrEmpty(setting.MemorySuffix)
                ? GetDefaultMemorySuffix(memoryUnit)
                : setting.MemorySuffix,
            ShowUserNamePrefix = setting.ShowUserNamePrefix
        };
    }

    /// <summary>Returns independent normalized copies of every catalog column.</summary>
    public static List<ProcessColumnSetting> CloneList(IEnumerable<ProcessColumnSetting>? source) =>
        Normalize(source);

    /// <summary>Returns a normalized layout with one persisted column width replaced.</summary>
    public static List<ProcessColumnSetting> WithWidth(
        IEnumerable<ProcessColumnSetting>? source,
        ProcessTableColumnKind column,
        double width)
    {
        List<ProcessColumnSetting> normalized = Normalize(source);
        if (!Enum.IsDefined(column)) return normalized;

        ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(column);
        for (int settingIndex = 0; settingIndex < normalized.Count; settingIndex++)
        {
            ProcessColumnSetting setting = normalized[settingIndex];
            if (setting.Column != column) continue;

            setting.Width = NormalizeWidth(width, definition.DefaultWidth);
            break;
        }

        return normalized;
    }

    /// <summary>Moves one visible column while leaving every hidden column in its existing slot.</summary>
    public static List<ProcessColumnSetting> MoveVisible(
        IEnumerable<ProcessColumnSetting>? source,
        ProcessTableColumnKind column,
        int insertionIndex)
    {
        List<ProcessColumnSetting> normalized = Normalize(source);
        List<ProcessColumnSetting> visible = new(normalized.Count);
        int sourceVisibleIndex = -1;
        for (int settingIndex = 0; settingIndex < normalized.Count; settingIndex++)
        {
            ProcessColumnSetting setting = normalized[settingIndex];
            if (!setting.Visible) continue;
            if (setting.Column == column) sourceVisibleIndex = visible.Count;
            visible.Add(setting);
        }

        if (sourceVisibleIndex < 0 || visible.Count < 2) return normalized;

        int targetVisibleIndex = Math.Clamp(insertionIndex, min: 0, visible.Count - 1);
        if (targetVisibleIndex == sourceVisibleIndex) return normalized;

        ProcessColumnSetting moved = visible[sourceVisibleIndex];
        visible.RemoveAt(sourceVisibleIndex);
        visible.Insert(targetVisibleIndex, moved);

        int visibleIndex = 0;
        for (int settingIndex = 0; settingIndex < normalized.Count; settingIndex++)
        {
            if (!normalized[settingIndex].Visible) continue;
            normalized[settingIndex] = visible[visibleIndex];
            visibleIndex++;
        }

        return normalized;
    }

    /// <summary>Returns a normalized layout with one column's display options replaced.</summary>
    public static List<ProcessColumnSetting> WithProperties(
        IEnumerable<ProcessColumnSetting>? source,
        ProcessColumnSetting replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        List<ProcessColumnSetting> normalized = Normalize(source);
        if (!Enum.IsDefined(replacement.Column)) return normalized;

        ProcessColumnSetting replacementClone = Clone(replacement);
        for (int settingIndex = 0; settingIndex < normalized.Count; settingIndex++)
        {
            ProcessColumnSetting setting = normalized[settingIndex];
            if (setting.Column != replacement.Column) continue;

            setting.Nickname = replacementClone.Nickname;
            setting.ShowPercentSuffix = replacementClone.ShowPercentSuffix;
            setting.ShowDecimalUsage = replacementClone.ShowDecimalUsage;
            setting.MemoryUnit = replacementClone.MemoryUnit;
            setting.MemorySuffix = replacementClone.MemorySuffix;
            setting.ShowUserNamePrefix = replacementClone.ShowUserNamePrefix;
            break;
        }

        return normalized;
    }

    /// <summary>Returns the effective header text for a column.</summary>
    public static string ResolveTitle(ProcessColumnSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        return string.IsNullOrWhiteSpace(setting.Nickname)
            ? ProcessTableColumnCatalog.Get(setting.Column).Title
            : setting.Nickname;
    }

    /// <summary>Returns whether a column represents a physical or accelerator memory quantity.</summary>
    public static bool IsMemoryColumn(ProcessTableColumnKind column) => column switch
    {
        ProcessTableColumnKind.WorkingSet
            or ProcessTableColumnKind.PeakWorkingSet
            or ProcessTableColumnKind.WorkingSetDelta
            or ProcessTableColumnKind.ActivePrivateWorkingSet
            or ProcessTableColumnKind.PrivateMemory
            or ProcessTableColumnKind.SharedWorkingSet
            or ProcessTableColumnKind.CommitSize
            or ProcessTableColumnKind.PagedPool
            or ProcessTableColumnKind.NonPagedPool
            or ProcessTableColumnKind.DedicatedGPUMemory
            or ProcessTableColumnKind.SharedGPUMemory
            or ProcessTableColumnKind.DedicatedNPUMemory
            or ProcessTableColumnKind.SharedNPUMemory => true,
        _ => false
    };

    /// <summary>Returns the suffix selected by default for a memory unit.</summary>
    public static string GetDefaultMemorySuffix(ProcessMemoryUnit unit) => NormalizeMemoryUnit(unit) switch
    {
        ProcessMemoryUnit.Kilobytes => "K",
        ProcessMemoryUnit.Megabytes => "M",
        ProcessMemoryUnit.Gigabytes => "G",
        ProcessMemoryUnit.PercentageOfSystem => "%",
        _ => "K"
    };

    private static ProcessMemoryUnit NormalizeMemoryUnit(ProcessMemoryUnit unit) =>
        Enum.IsDefined(unit) ? unit : ProcessMemoryUnit.Kilobytes;

    private static double NormalizeWidth(double width, double defaultWidth) =>
        double.IsFinite(width) && width >= MinimumWidth ? width : defaultWidth;
}
