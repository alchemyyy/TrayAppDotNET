using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BatteryTrayAppDotNET.UI.Flyout;

public sealed partial class BatteryFlyoutResources : ResourceDictionary
{
#if DEBUG
    private static readonly AXAMLResourceHotReloadStore<BatteryFlyoutResources> Resources =
        AXAMLResourceHotReloadStore<BatteryFlyoutResources>.Create(
            resourceName: "Battery flyout resources",
            static () => new BatteryFlyoutResources(),
            NotifyResourcesReloaded,
            sourceFileName: "BatteryFlyoutWindow.axaml");
#else
    private static readonly Lazy<BatteryFlyoutResources> Resources = new(static () => new BatteryFlyoutResources());
#endif

    public BatteryFlyoutResources() => AvaloniaXamlLoader.Load(this);

    internal static BatteryFlyoutResources Current
    {
        get
        {
#if DEBUG
            return Resources.Current;
#else
            return Resources.Value;
#endif
        }
    }

#if DEBUG
    internal static event Action? ResourcesReloaded;

    private static void NotifyResourcesReloaded()
    {
        if (ResourcesReloaded is not { } handlers) return;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try { ((Action)handler)(); }
            catch (Exception exception)
            {
                TADNLog.LogDebug($"Battery flyout AXAML hot-reload notification failed: {exception.Message}");
            }
        }
    }
#endif
}
