using Avalonia.Controls;

namespace FanControlTrayAppDotNET.UI.Flyout;

/// <summary>Layout primitives for fan flyout cells; the open flyout rebuilds on <c>ResourcesReloaded</c>.</summary>
[AxamlResourceStore(AxamlReloadMode.Replaced, "Fan flyout cell resources")]
public sealed partial class FanFlyoutCellResources : ResourceDictionary;
