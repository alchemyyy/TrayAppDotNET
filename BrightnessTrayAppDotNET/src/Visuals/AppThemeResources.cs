using Avalonia.Controls;
using TrayAppDotNETCommon.UI;

namespace BrightnessTrayAppDotNET.Visuals;

/// <summary>
/// The brightness theme-color defaults; <see cref="AppTheme"/> reads them through the generated getters.
/// </summary>
[AxamlResourceStore(AxamlReloadMode.AppTheme, "Brightness theme color catalog")]
public sealed partial class AppThemeResources : ResourceDictionary;
