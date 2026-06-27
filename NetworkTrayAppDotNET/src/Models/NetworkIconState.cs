namespace NetworkTrayAppDotNET.Models;

/// <summary>
/// The discrete network states the tray icon visualizes.
/// Wi-Fi has two parallel ladders (with-internet and no-internet) at 0-4 bars;
/// Ethernet has connected / no-internet / disconnected;
/// the rest are transient or fallback.
/// </summary>
public enum NetworkIconState
{
    NoNetwork,
    EthernetConnected,
    EthernetNoInternet,
    EthernetDisconnected,
    WifiDisconnected,
    WifiConnecting,
    Wifi0Bars,
    Wifi1Bar,
    Wifi2Bars,
    Wifi3Bars,
    Wifi4Bars,
    Wifi0BarsNoInternet,
    Wifi1BarNoInternet,
    Wifi2BarsNoInternet,
    Wifi3BarsNoInternet,
    Wifi4BarsNoInternet,
}

/// <summary>
/// Which UI to surface on tray left-click.
/// COM-backed Windows10 / Windows11 paths can fail on locked-down builds; URI variants are reliable fallbacks.
/// </summary>
public enum FlyoutStyle
{
    // Win10 NetworkFlyoutExperienceManager via shell COM
    Windows10,

    // Win11 ControlCenterExperienceManager via shell COM
    Windows11,

    // ms-actioncenter:controlcenter URI
    QuickSettings,

    // ms-availablenetworks: URI
    AvailableNetworks,

    // ms-settings:network-wifi URI
    Settings,
}

/// <summary>
/// Which window to open when the user picks "Adapter Settings" from the tray menu.
/// </summary>
public enum AdapterSettingsStyle
{
    // Classic Control Panel applet (ncpa.cpl).
    ControlPanel,

    // Network Connections opened as a folder via shell GUID under Explorer.
    Explorer,
}
