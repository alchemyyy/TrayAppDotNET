namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Creates the painted scrollbar styles shared by Task Manager tables and lists.</summary>
internal static class TaskManagerScrollBarStyles
{
    /// <summary>Creates the process-grid scrollbar appearance.</summary>
    internal static SettingsScrollBarStyle CreateProcessGrid(TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return new SettingsScrollBarStyle(
            resources.AxamlProcessTable.ScrollBarTrackThickness,
            resources.AxamlProcessTable.ScrollBarIdleThumbThickness,
            resources.AxamlProcessTable.ScrollBarHoverThumbThickness,
            resources.AxamlProcessTable.ScrollBarThumbEndMargin,
            resources.AxamlProcessTable.ScrollBarMinimumThumbLength,
            resources.AxamlProcessTable.GridBackgroundColor,
            resources.AxamlProcessTable.ScrollThumbColor,
            resources.AxamlProcessTable.ScrollHoverThumbColor,
            resources.AxamlProcessTable.ScrollHoverThumbColor,
            resources.AxamlProcessTable.ScrollHoverThumbColor,
            ShowButtonsOnHover: true);
    }
}
