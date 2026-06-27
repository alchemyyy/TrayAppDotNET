using NetworkTrayAppDotNET.Models;
using Windows.Networking.Connectivity;

namespace NetworkTrayAppDotNET.Services;

/// <summary>
/// Watches Windows network connectivity via WinRT and surfaces a single rolled-up
/// NetworkIconState plus a per-connection tooltip string. Fires NetworkStateChanged
/// only when the rolled-up state actually changes (callers Dispatcher-marshal).
/// </summary>
public sealed class NetworkMonitor : IDisposable
{
    private bool _disposed;
    private readonly NetworkStatusChangedEventHandler _networkHandler;

    public event Action<NetworkIconState>? NetworkStateChanged;

    public NetworkIconState CurrentState { get; private set; } = NetworkIconState.NoNetwork;
    public int WifiSignalBars { get; private set; }
    public string CurrentNetworkName { get; private set; } = string.Empty;
    public List<(string Name, bool IsWifi, bool HasInternet)> AllConnections { get; private set; } = [];

    public NetworkMonitor() => _networkHandler = _ => RefreshState();

    public void Initialize()
    {
        NetworkInformation.NetworkStatusChanged += _networkHandler;
        RefreshState();
    }

    public void RefreshState()
    {
        NetworkIconState previousState = CurrentState;
        List<(string Name, bool IsWifi, bool HasInternet)> connections = [];

        try
        {
            ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile == null)
            {
                UpdateState(NetworkIconState.NoNetwork, 0, string.Empty, connections, previousState);
                return;
            }

            NetworkConnectivityLevel connectivity = profile.GetNetworkConnectivityLevel();
            string networkName = profile.ProfileName?.Trim() ?? string.Empty;

            // GetSignalBars returns 1-5 for Wi-Fi, null for non-Wi-Fi (Ethernet, etc.)
            byte? signalBars = profile.GetSignalBars();

            // Build per-connection list for tooltip display
            foreach (ConnectionProfile? p in NetworkInformation.GetConnectionProfiles())
            {
                NetworkConnectivityLevel level = p.GetNetworkConnectivityLevel();
                if (level == NetworkConnectivityLevel.None) continue;

                string name = p.ProfileName?.Trim() ?? "Unknown";
                bool isWifi = p.GetSignalBars() != null;
                bool hasInternet = level == NetworkConnectivityLevel.InternetAccess;
                connections.Add((name, isWifi, hasInternet));
            }

            if (signalBars != null)
            {
                int bars = Math.Clamp((int)signalBars.Value, 1, 4);

                bool hasInternet = connectivity == NetworkConnectivityLevel.InternetAccess;
                NetworkIconState newState = (hasInternet, bars) switch
                {
                    (true, 0) => NetworkIconState.Wifi0Bars,
                    (true, 1) => NetworkIconState.Wifi1Bar,
                    (true, 2) => NetworkIconState.Wifi2Bars,
                    (true, 3) => NetworkIconState.Wifi3Bars,
                    (true, _) => NetworkIconState.Wifi4Bars,
                    (false, 0) => NetworkIconState.Wifi0BarsNoInternet,
                    (false, 1) => NetworkIconState.Wifi1BarNoInternet,
                    (false, 2) => NetworkIconState.Wifi2BarsNoInternet,
                    (false, 3) => NetworkIconState.Wifi3BarsNoInternet,
                    _ => NetworkIconState.Wifi4BarsNoInternet,
                };
                UpdateState(newState, bars, networkName, connections, previousState);
                return;
            }

            // No signal bars = Ethernet or other wired connection
            NetworkIconState ethernetState = connectivity switch
            {
                NetworkConnectivityLevel.InternetAccess => NetworkIconState.EthernetConnected,
                NetworkConnectivityLevel.LocalAccess
                    or NetworkConnectivityLevel.ConstrainedInternetAccess => NetworkIconState.EthernetNoInternet,
                _ => NetworkIconState.EthernetDisconnected,
            };
            UpdateState(ethernetState, 0, networkName, connections, previousState);
        }
        catch
        {
            UpdateState(NetworkIconState.NoNetwork, 0, string.Empty, connections, previousState);
        }
    }

    private void UpdateState(NetworkIconState state, int bars, string name,
        List<(string, bool, bool)> connections, NetworkIconState previous)
    {
        CurrentState = state;
        WifiSignalBars = bars;
        CurrentNetworkName = name;
        AllConnections = connections;
        if (state != previous) NetworkStateChanged?.Invoke(state);
    }

    public string GetTooltipText()
    {
        if (AllConnections.Count > 0)
        {
            IEnumerable<string> entries = AllConnections.Select(c =>
                $"{c.Name}\r\n{(c.HasInternet ? "Internet access" : "No internet")}");
            return string.Join("\r\n\r\n", entries);
        }

        return CurrentState switch
        {
            NetworkIconState.WifiDisconnected => "Wi-Fi\r\nDisconnected",
            NetworkIconState.WifiConnecting => "Wi-Fi\r\nConnecting...",
            NetworkIconState.EthernetDisconnected => "Ethernet\r\nDisconnected",
            _ => "No network connection",
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        NetworkInformation.NetworkStatusChanged -= _networkHandler;
    }
}
