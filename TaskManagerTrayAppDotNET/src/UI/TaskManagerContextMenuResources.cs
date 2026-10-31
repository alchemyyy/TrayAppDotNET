using Avalonia.Controls;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>
/// Visual primitives for Task Manager content context menus; live menus and tables refresh on <c>ResourcesReloaded</c>.
/// </summary>
[AxamlResourceStore(AxamlReloadMode.Synchronized, "Task Manager context menu resources")]
public sealed partial class TaskManagerContextMenuResources : ResourceDictionary;
