using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>
/// Layout primitives for the common confirmation window; open windows rebuild on <c>ResourcesReloaded</c>.
/// </summary>
[AxamlResourceStore(AxamlReloadMode.Replaced, "Confirmation window resources")]
public sealed partial class ConfirmationWindowResources : ResourceDictionary;
