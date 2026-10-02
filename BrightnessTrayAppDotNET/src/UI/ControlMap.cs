using Avalonia.Markup.Xaml;

namespace BrightnessTrayAppDotNET.UI;

/// <summary>
/// Every interactive Brightness control in tab order, with its focus scope and key gestures.
/// </summary>
public sealed partial class ControlMap : TrayAppDotNETCommon.UI.ControlMapping.ControlMap
{
    /// <summary>
    /// Builds the node tree from the compiled control map AXAML.
    /// </summary>
    public ControlMap() : base(MapName) => AvaloniaXamlLoader.Load(this);
}
