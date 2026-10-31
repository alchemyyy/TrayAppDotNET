using Avalonia.Controls;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Layout primitives for Task Manager reorder lists; open lists rebuild on <c>ResourcesReloaded</c>.</summary>
[AxamlResourceStore(AxamlReloadMode.Replaced, "Task Manager reorder resources")]
public sealed partial class TaskManagerReorderResources : ResourceDictionary;
