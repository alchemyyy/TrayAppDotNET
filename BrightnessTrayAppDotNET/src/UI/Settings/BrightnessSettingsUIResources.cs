using Avalonia.Controls;
using TrayAppDotNETCommon.UI;

namespace BrightnessTrayAppDotNET.UI.Settings;

/// <summary>
/// Layout primitives for the brightness settings window; open windows rebuild on <c>ResourcesReloaded</c>.
/// </summary>
[AxamlResourceStore(AxamlReloadMode.Synchronized, "Brightness settings UI resources")]
public sealed partial class BrightnessSettingsUIResources : ResourceDictionary;
