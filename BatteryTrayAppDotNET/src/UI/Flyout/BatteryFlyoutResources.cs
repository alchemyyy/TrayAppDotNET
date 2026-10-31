using Avalonia.Controls;

namespace BatteryTrayAppDotNET.UI.Flyout;

/// <summary>
/// Layout primitives for the battery flyout and health window; open windows rebuild on <c>ResourcesReloaded</c>.
/// </summary>
[AxamlResourceStore(AxamlReloadMode.Replaced, "Battery flyout resources")]
public sealed partial class BatteryFlyoutResources : ResourceDictionary;
