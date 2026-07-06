using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class UpdateConfirmationWindowResources : ResourceDictionary
{
    /// <summary>
    /// Initializes the compiled update-confirmation resource dictionary.
    /// </summary>
    public UpdateConfirmationWindowResources() => AvaloniaXamlLoader.Load(this);
}
