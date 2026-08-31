#if DEBUG
namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Captures the AXAML-backed default widths that can change while Processes is open.</summary>
internal readonly record struct ProcessTableAXAMLColumnWidths(
    double Name,
    double ProcessID,
    double Status,
    double UserName,
    double CPU,
    double Lifetime,
    double PrivateMemory,
    double WorkingSet,
    double CommandLine)
{
    public bool TryGet(ProcessTableColumnKind column, out double width)
    {
        width = column switch
        {
            ProcessTableColumnKind.Name => Name,
            ProcessTableColumnKind.ProcessID => ProcessID,
            ProcessTableColumnKind.Status => Status,
            ProcessTableColumnKind.UserName => UserName,
            ProcessTableColumnKind.CPU => CPU,
            ProcessTableColumnKind.Lifetime => Lifetime,
            ProcessTableColumnKind.PrivateMemory => PrivateMemory,
            ProcessTableColumnKind.WorkingSet or ProcessTableColumnKind.SharedWorkingSet => WorkingSet,
            ProcessTableColumnKind.CommandLine => CommandLine,
            _ => double.NaN
        };
        return double.IsFinite(width);
    }
}

/// <summary>Applies changed AXAML widths without mutating the caller's current settings.</summary>
internal static class ProcessTableAXAMLHotReload
{
    private const double WidthEqualityTolerance = 0.01;

    public static bool TryApplyColumnWidths(
        IReadOnlyList<ProcessColumnSetting> settings,
        ProcessTableAXAMLColumnWidths currentWidths,
        ProcessTableAXAMLColumnWidths nextWidths,
        out List<ProcessColumnSetting> updatedSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        updatedSettings = ProcessColumnSettings.CloneList(settings);
        bool changed = false;
        for (int settingIndex = 0; settingIndex < updatedSettings.Count; settingIndex++)
        {
            ProcessColumnSetting setting = updatedSettings[settingIndex];
            if (!currentWidths.TryGet(setting.Column, out double currentWidth)
                || !nextWidths.TryGet(setting.Column, out double nextWidth)
                || Math.Abs(currentWidth - nextWidth) < WidthEqualityTolerance)
                continue;

            setting.Width = Math.Max(ProcessColumnSettings.MinimumWidth, nextWidth);
            changed = true;
        }

        return changed;
    }
}
#endif
