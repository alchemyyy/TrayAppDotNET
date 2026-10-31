using Avalonia.Controls;
using TrayAppDotNETCommon.UI;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>The shared theme-color defaults; <see cref="AppTheme"/> reads them through the generated getters.</summary>
[AxamlResourceStore(AxamlReloadMode.AppTheme, "Common theme color catalog")]
public sealed partial class AppThemeResources : ResourceDictionary;
