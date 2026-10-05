using Windows.Networking.Connectivity;
using NetworkTrayAppDotNET.Models;

namespace NetworkTrayAppDotNET.Services;

/// <summary>
/// Watches Windows network connectivity via WinRT and surfaces a single rolled-up
/// NetworkIconState plus a per-connection tooltip string. Fires NetworkStateChanged
/// only when the rolled-up state actually changes (callers Dispatcher-marshal).
/// While no connection reaches the internet, the Wi-Fi adapter state from WLAN AutoConfig
/// picks between the Wi-Fi connecting, Wi-Fi disconnected and no network states.
/// </summary>
public sealed class NetworkMonitor : IDisposable
{
    private readonly NetworkStatusChangedEventHandler _networkHandler;
    private readonly WifiAdapterMonitor _wifiAdapters = new();
    private readonly Lock _refreshGate = new();
    private int _refreshPending;
    private int _disposed;
    private int _initialized;

    public event Action<NetworkIconState>? NetworkStateChanged;

    public NetworkIconState CurrentState { get; private set; } = NetworkIconState.NoNetwork;
    public List<(string Name, bool IsWifi, bool HasInternet)> AllConnections { get; private set; } = [];

    public NetworkMonitor() => _networkHandler = _ => RefreshState();

    public void Initialize()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _initialized, value: 1) != 0) return;

        try
        {
            NetworkInformation.NetworkStatusChanged += _networkHandler;
            _wifiAdapters.StateChanged += OnWifiAdapterStateChanged;
            RefreshState();
        }
        catch (Exception ex)
        {
            try { NetworkInformation.NetworkStatusChanged -= _networkHandler; }
            catch (Exception unsubscribeException)
            {
                TADNLog.Log($"NetworkMonitor.Initialize unsubscribe: {unsubscribeException.Message}");
            }

            _wifiAdapters.StateChanged -= OnWifiAdapterStateChanged;
            Interlocked.Exchange(ref _initialized, value: 0);
            TADNLog.Log($"NetworkMonitor.Initialize: {ex.Message}");
            throw;
        }
    }

    public void RefreshState()
    {
        // Poll ticks, NetworkStatusChanged and Wi-Fi notifications never wait on each other: a request that arrives
        // while a refresh runs is picked up by that refresh before it releases the gate
        Interlocked.Exchange(ref _refreshPending, value: 1);
        while (Volatile.Read(ref _refreshPending) != 0 && _refreshGate.TryEnter())
        {
            try
            {
                while (Interlocked.Exchange(ref _refreshPending, value: 0) != 0)
                    RefreshSnapshot();
            }
            finally
            {
                _refreshGate.Exit();
            }
        }
    }

    private void RefreshSnapshot()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        NetworkIconState previousState = CurrentState;

        try
        {
            NetworkSnapshot snapshot = BuildSnapshot(_wifiAdapters);
            UpdateState(snapshot.State, snapshot.Connections, previousState);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkMonitor.RefreshState: {ex.Message}");
            UpdateState(NetworkIconState.NoNetwork, [], previousState);
        }
        finally
        {
            ReleaseWinRTProjectionReferencesAfterRefresh();
        }
    }

    private static NetworkSnapshot BuildSnapshot(WifiAdapterMonitor wifiAdapters)
    {
        List<(string Name, bool IsWifi, bool HasInternet)> connections = [];
        ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile == null)
        {
            NetworkIconState adapterState = wifiAdapters.ReadActivity() switch
            {
                WifiAdapterActivity.Connecting => NetworkIconState.WifiConnecting,
                WifiAdapterActivity.Disconnected => NetworkIconState.WifiDisconnected,
                _ => NetworkIconState.NoNetwork
            };
            return new NetworkSnapshot(adapterState, connections);
        }

        NetworkConnectivityLevel connectivity = profile.GetNetworkConnectivityLevel();

        // GetSignalBars returns 0-5 for Wi-Fi, null for non-Wi-Fi such as Ethernet
        byte? signalBars = profile.GetSignalBars();

        // Build per-connection list for tooltip display
        foreach (ConnectionProfile? connectionProfile in NetworkInformation.GetConnectionProfiles())
        {
            NetworkConnectivityLevel level = connectionProfile.GetNetworkConnectivityLevel();
            if (level == NetworkConnectivityLevel.None) continue;

            string name = connectionProfile.ProfileName?.Trim() ?? "Unknown";
            bool isWifi = connectionProfile.GetSignalBars() != null;
            bool hasInternet = level == NetworkConnectivityLevel.InternetAccess;
            connections.Add((name, isWifi, hasInternet));
        }

        if (signalBars != null)
        {
            // The icon ladders end at 4 bars, so a 5-bar reading shows as full signal
            int bars = Math.Clamp((int)signalBars.Value, min: 0, max: 4);
            bool hasInternet = connectivity == NetworkConnectivityLevel.InternetAccess;
            NetworkIconState state = (hasInternet, bars) switch
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
                _ => NetworkIconState.Wifi4BarsNoInternet
            };
            return new NetworkSnapshot(state, connections);
        }

        // No signal bars = Ethernet or other wired connection
        NetworkIconState ethernetState = connectivity switch
        {
            NetworkConnectivityLevel.InternetAccess => NetworkIconState.EthernetConnected,
            NetworkConnectivityLevel.LocalAccess
                or NetworkConnectivityLevel.ConstrainedInternetAccess => NetworkIconState.EthernetNoInternet,
            _ => NetworkIconState.EthernetDisconnected
        };
        return new NetworkSnapshot(ethernetState, connections);
    }

    /// <summary>
    /// Forces C#/WinRT projection wrappers to release native COM references after each refresh.
    /// </summary>
    private static void ReleaseWinRTProjectionReferencesAfterRefresh()
    {
        // ConnectionProfile and the profile list returned by GetConnectionProfiles do not implement
        // IDisposable. Long-run dumps showed ConnectionProfileServer and CAgileReferenceMarshaled buildup
        // after repeated refreshes. Refreshes are low frequency, so a full blocking GC is acceptable for now.
        // TODO network: replace this hot path with lower-level APIs that expose explicit native ownership.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
    }

    private void UpdateState(NetworkIconState state, List<(string, bool, bool)> connections,
        NetworkIconState previous)
    {
        CurrentState = state;
        AllConnections = connections;
        if (state != previous) NetworkStateChanged?.Invoke(state);
    }

    // WLAN AutoConfig raises notifications on its own callback thread, which must not run a refresh
    private void OnWifiAdapterStateChanged() =>
        ThreadPool.QueueUserWorkItem(static monitor =>
        {
            try { monitor.RefreshState(); }
            catch (Exception ex) { TADNLog.Log($"NetworkMonitor Wi-Fi notification refresh: {ex.Message}"); }
        }, this, preferLocal: false);

    public string GetTooltipText()
    {
        if (AllConnections.Count > 0)
        {
            IEnumerable<string> entries = AllConnections.Select(c =>
                $"{c.Name}\r\n{(c.HasInternet ? "Internet access" : "No internet")}");
            return string.Join(separator: "\r\n\r\n", entries);
        }

        return CurrentState switch
        {
            NetworkIconState.WifiDisconnected => "Wi-Fi\r\nDisconnected",
            NetworkIconState.WifiConnecting => "Wi-Fi\r\nConnecting...",
            NetworkIconState.EthernetDisconnected => "Ethernet\r\nDisconnected",
            _ => "No network connection"
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

        if (Interlocked.Exchange(ref _initialized, value: 0) != 0)
        {
            try { NetworkInformation.NetworkStatusChanged -= _networkHandler; }
            catch (Exception ex)
            {
                TADNLog.Log($"NetworkMonitor.Dispose unsubscribe: {ex.Message}");
            }
        }

        _wifiAdapters.StateChanged -= OnWifiAdapterStateChanged;
        _wifiAdapters.Dispose();
        NetworkStateChanged = null;
    }

    private readonly record struct NetworkSnapshot(
        NetworkIconState State,
        List<(string Name, bool IsWifi, bool HasInternet)> Connections);
}
