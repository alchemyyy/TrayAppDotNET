#if DEBUG
namespace TrayAppDotNETCommon.UI;

/// <summary>Notifies live common UI surfaces after a shared AXAML dictionary reloads.</summary>
public static class CommonAXAMLHotReload
{
    public static event Action? ResourcesReloaded;

    /// <summary>Raises <see cref="ResourcesReloaded"/> for a reloaded common dictionary.</summary>
    public static void NotifyResourcesReloaded(string label) => AXAMLHotReload.NotifyEach(ResourcesReloaded, label);
}
#endif
