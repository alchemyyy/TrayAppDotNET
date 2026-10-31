using Avalonia.Controls;

namespace NetworkTrayAppDotNET.Visuals;

/// <summary>
/// The network theme-color defaults; <see cref="AppTheme"/> reads them through the generated getters.
/// </summary>
[AxamlResourceStore(AxamlReloadMode.AppTheme, "Network theme color catalog")]
public sealed partial class AppThemeResources : ResourceDictionary;
