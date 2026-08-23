using System.Xml.Serialization;
using TaskManagerTrayAppDotNET.UI;

namespace TaskManagerTrayAppDotNET.Models;

public sealed class ProcessColumnSetting
{
    [XmlAttribute]
    public ProcessTableColumnKind Column { get; set; }

    [XmlAttribute]
    public bool Visible { get; set; }

    [XmlAttribute]
    public double Width { get; set; }
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
                Column = definition.Kind,
                Visible = definition.DefaultVisible,
                Width = definition.DefaultWidth
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
                normalized.Add(new ProcessColumnSetting
                {
                    Column = setting.Column,
                    Visible = setting.Visible,
                    Width = NormalizeWidth(setting.Width, definition.DefaultWidth)
                });
            }
        }

        foreach (ProcessTableColumnDefinition definition in ProcessTableColumnCatalog.Definitions)
        {
            if (!used.Add(definition.Kind)) continue;

            normalized.Add(new ProcessColumnSetting
            {
                Column = definition.Kind,
                Visible = definition.DefaultVisible,
                Width = definition.DefaultWidth
            });
        }

        if (!normalized.Any(static setting => setting.Visible))
            normalized[0].Visible = true;
        return normalized;
    }

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

        int targetVisibleIndex = Math.Clamp(insertionIndex, 0, visible.Count - 1);
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

    private static double NormalizeWidth(double width, double defaultWidth) =>
        double.IsFinite(width) && width >= MinimumWidth ? width : defaultWidth;
}
