using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI;

/// <summary>Layout primitives for common settings bindings; each settings window merges its own instance.</summary>
[AxamlResourceStore(AxamlReloadMode.CommonPerOwner, "Common binding resources")]
public sealed partial class CommonBindingsResources : ResourceDictionary;
