using Avalonia.Controls;

namespace BatteryTrayAppDotNET.Visuals;

/// <summary>
/// The battery theme-color defaults; <see cref="AppTheme"/> reads them through the generated getters.
/// </summary>
[AxamlResourceStore(AxamlReloadMode.AppTheme, "Battery theme color catalog")]
public sealed partial class AppThemeResources : ResourceDictionary;
