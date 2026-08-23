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

    private static double NormalizeWidth(double width, double defaultWidth) =>
        double.IsFinite(width) && width >= 40 ? width : defaultWidth;
}
