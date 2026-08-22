using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

public sealed partial class TaskManagerWindowResources : ResourceDictionary
{
    public static readonly Color ProcessGridBackgroundColor = Color.FromRgb(0x19, 0x19, 0x19);
    public static readonly Color ProcessGridScrollThumbColor = Color.FromRgb(0x8A, 0x8A, 0x8A);
    public static readonly Color ProcessGridScrollHoverThumbColor = Color.FromRgb(0xA6, 0xA6, 0xA6);
    public static readonly Color ProcessGridResizeGripColor = Color.FromRgb(0x8A, 0x8A, 0x8A);

    /// <summary>Initializes the compiled Task Manager window resource dictionary.</summary>
    public TaskManagerWindowResources() => AvaloniaXamlLoader.Load(this);
}
