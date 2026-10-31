namespace NetworkTrayAppDotNET.Models;

/// <summary>
/// Network's hotkey actions. OpenNetworkSettings and OpenAdapterSettings are bindable only in settings.xml; the
/// Hotkeys page lists OpenFlyout and OpenSettings. Member names are persisted.
/// </summary>
public enum NetworkHotkeyAction
{
    OpenSettings,
    OpenFlyout,
    OpenNetworkSettings,
    OpenAdapterSettings
}

/// <inheritdoc cref="HotkeyBindingBase{TAction}"/>
public sealed class NetworkHotkeyBinding : HotkeyBindingBase<NetworkHotkeyAction>;
