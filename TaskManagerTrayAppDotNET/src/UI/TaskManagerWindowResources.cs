using Avalonia.Controls;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>
/// Layout and palette primitives for the Task Manager window; code-created controls refresh on
/// <c>ResourcesReloaded</c>.
/// </summary>
[AxamlResourceStore(AxamlReloadMode.Synchronized, "Task Manager window resources")]
public sealed partial class TaskManagerWindowResources : ResourceDictionary
{
    internal Color GridBackgroundColor(bool isLightTheme) => isLightTheme
        ? AxamlProcessTable.LightGridBackgroundColor
        : AxamlProcessTable.DarkGridBackgroundColor;

    internal Color ScrollHoverThumbColor(bool isLightTheme) => isLightTheme
        ? AxamlProcessTable.LightScrollHoverThumbColor
        : AxamlProcessTable.DarkScrollHoverThumbColor;

    internal Color GraphBackgroundColor(bool isLightTheme) => isLightTheme
        ? AxamlTaskManagerPerformance.LightGraphBackgroundColor
        : AxamlTaskManagerPerformance.DarkGraphBackgroundColor;

    internal Color GraphHoverLineColor(bool isLightTheme) => isLightTheme
        ? AxamlTaskManagerPerformance.LightGraphHoverLineColor
        : AxamlTaskManagerPerformance.DarkGraphHoverLineColor;
}
