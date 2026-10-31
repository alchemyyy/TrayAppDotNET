using Windows.Networking.Connectivity;
using NetworkTrayAppDotNET.Models;

namespace NetworkTrayAppDotNET.Services;

/// <summary>
/// Watches Windows network connectivity via WinRT and surfaces a single rolled-up
/// NetworkIconState plus a per-connection tooltip string. Every refresh runs on a thread-pool thread (poll ticks
/// through <see cref="RequestRefresh"/>, NetworkStatusChanged and Wi-Fi notifications alike), and
/// NetworkStateChanged fires there when the state or the connection list changed (callers Dispatcher-marshal).
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
    private long _lastProjectionReleaseTick;

    public event Action<NetworkIconState>? NetworkStateChanged;

    public NetworkIconState CurrentState { get; private set; } = NetworkIconState.NoNetwork;
    public List<(string Name, bool IsWifi, bool HasInternet)> AllConnections { get; private set; } = [];

    public NetworkMonitor() => _networkHandler = _ => RequestRefresh();

    public void Initialize()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _initialized, value: 1) != 0) return;

        try
        {
            NetworkInformation.NetworkStatusChanged += _networkHandler;
            _wifiAdapters.StateChanged += OnWifiAdapterStateChanged;
            RequestRefresh();
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

    /// <summary>Queues a refresh on the thread pool; the caller never waits for WinRT.</summary>
    public void RequestRefresh() =>
        ThreadPool.QueueUserWorkItem(static monitor =>
        {
            try { monitor.RefreshState(); }
            catch (Exception ex) { TADNLog.Log($"NetworkMonitor queued refresh: {ex.Message}"); }
        }, this, preferLocal: false);

    private void RefreshState()
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
        bool changed;

        try
        {
            NetworkSnapshot snapshot = BuildSnapshot(_wifiAdapters);
            changed = UpdateState(snapshot.State, snapshot.Connections, previousState);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkMonitor.RefreshState: {ex.Message}");
            changed = UpdateState(NetworkIconState.NoNetwork, [], previousState);
        }

        // Profiles churn when something changed; otherwise the wrappers only need releasing now and then
        long now = Environment.TickCount64;
        if (changed || now - _lastProjectionReleaseTick >= TimeConstants.NetworkProjectionReleaseIntervalMs)
        {
            _lastProjectionReleaseTick = now;
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
    /// Forces C#/WinRT projection wrappers to release native COM references. Runs on the refreshing pool thread, after
    /// a change or at most every <see cref="TimeConstants.NetworkProjectionReleaseIntervalMs"/>.
    /// </summary>
    private static void ReleaseWinRTProjectionReferencesAfterRefresh()
    {
        // ConnectionProfile and the profile list returned by GetConnectionProfiles do not implement
        // IDisposable. Long-run dumps showed ConnectionProfileServer and CAgileReferenceMarshaled buildup
        // after repeated refreshes, which only a full collection releases.
        // TODO network: replace this hot path with lower-level APIs that expose explicit native ownership.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
    }

    private bool UpdateState(NetworkIconState state, List<(string Name, bool IsWifi, bool HasInternet)> connections,
        NetworkIconState previous)
    {
        bool changed = state != previous || !connections.SequenceEqual(AllConnections);
        CurrentState = state;
        AllConnections = connections;
        if (changed) NetworkStateChanged?.Invoke(state);
        return changed;
    }

    // WLAN AutoConfig raises notifications on its own callback thread, which must not run a refresh
    private void OnWifiAdapterStateChanged() => RequestRefresh();

    public string GetTooltipText()
    {
        if (AllConnections.Count > 0)
        {
            // Whole connections only, so the shell's tooltip limit never cuts one in half
            System.Text.StringBuilder text = new();
            foreach (string entry in AllConnections.Select(c =>
                         $"{c.Name}\n{(c.HasInternet ? "Internet access" : "No internet")}"))
            {
                int separatorLength = text.Length == 0 ? 0 : 2;
                if (text.Length + separatorLength + entry.Length > TrayTooltipText.MaxLength && text.Length > 0)
                    break;

                if (separatorLength > 0) text.Append("\n\n");
                text.Append(entry);
            }

            return text.ToString();
        }

        return CurrentState switch
        {
            NetworkIconState.WifiDisconnected => "Wi-Fi\nDisconnected",
            NetworkIconState.WifiConnecting => "Wi-Fi\nConnecting...",
            NetworkIconState.EthernetDisconnected => "Ethernet\nDisconnected",
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
