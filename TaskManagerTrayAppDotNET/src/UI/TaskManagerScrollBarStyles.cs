namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Creates the painted scrollbar styles shared by Task Manager tables and lists.</summary>
internal static class TaskManagerScrollBarStyles
{
    /// <summary>Creates the process-grid scrollbar appearance.</summary>
    internal static SettingsScrollBarStyle CreateProcessGrid(TaskManagerWindowResources resources, bool isLightTheme)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return new SettingsScrollBarStyle(
            resources.AxamlProcessTable.ScrollBarTrackThickness,
            resources.AxamlProcessTable.ScrollBarIdleThumbThickness,
            resources.AxamlProcessTable.ScrollBarHoverThumbThickness,
            resources.AxamlProcessTable.ScrollBarThumbEndMargin,
            resources.AxamlProcessTable.ScrollBarMinimumThumbLength,
            resources.GridBackgroundColor(isLightTheme),
            resources.AxamlProcessTable.ScrollThumbColor,
            resources.ScrollHoverThumbColor(isLightTheme),
            resources.ScrollHoverThumbColor(isLightTheme),
            resources.ScrollHoverThumbColor(isLightTheme),
            ShowButtonsOnHover: true);
    }
}
