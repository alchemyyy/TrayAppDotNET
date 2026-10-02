using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Templates for the interactive structure every app shares: the settings shell, common settings sections,
/// dialogs, and menus. App control maps instantiate them by id.
/// </summary>
public sealed partial class ControlMap : ControlMapping.ControlMap
{
    /// <summary>
    /// Builds the node tree from the compiled control map AXAML.
    /// </summary>
    public ControlMap() : base(MapName) => AvaloniaXamlLoader.Load(this);
}
