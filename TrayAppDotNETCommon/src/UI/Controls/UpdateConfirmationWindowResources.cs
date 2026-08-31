using Avalonia.Controls;
using Avalonia.Markup.Xaml;
#if DEBUG
#endif

namespace TrayAppDotNETCommon.UI.Controls;

public sealed partial class UpdateConfirmationWindowResources : ResourceDictionary
{
#if DEBUG
    private static readonly AXAMLResourceHotReloadStore<UpdateConfirmationWindowResources> Resources =
        AXAMLResourceHotReloadStore<UpdateConfirmationWindowResources>.Create(
            resourceName: "Update confirmation window resources",
            static () => new UpdateConfirmationWindowResources(),
            NotifyResourcesReloaded,
            sourceFileName: "UpdateConfirmationWindow.axaml");
#else
    private static readonly Lazy<UpdateConfirmationWindowResources> Resources =
        new(static () => new UpdateConfirmationWindowResources());
#endif

    /// <summary>
    /// Initializes the compiled update-confirmation resource dictionary.
    /// </summary>
    public UpdateConfirmationWindowResources() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the active compiled or hot-reloaded resource dictionary.</summary>
    internal static UpdateConfirmationWindowResources Current
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
    /// <summary>Notifies open update confirmation windows after a successful AXAML reload.</summary>
    internal static event Action? ResourcesReloaded;

    /// <summary>Reloads the source AXAML immediately for focused Debug verification.</summary>
    internal static void ReloadNow() => Resources.ReloadNow();

    private static void NotifyResourcesReloaded()
    {
        Action? handlers = ResourcesReloaded;
        if (handlers == null) return;

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception exception)
            {
                TADNLog.LogDebug(
                    $"Update confirmation AXAML hot-reload notification failed: {exception.Message}");
            }
        }
    }
#endif
}
