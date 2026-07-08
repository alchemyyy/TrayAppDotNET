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
    private readonly NetworkStatusChangedEventHandler _networkHandler;
    private int _disposed;
    private int _initialized;

    public event Action<NetworkIconState>? NetworkStateChanged;

    public NetworkIconState CurrentState { get; private set; } = NetworkIconState.NoNetwork;
    public int WifiSignalBars { get; private set; }
    public string CurrentNetworkName { get; private set; } = string.Empty;
    public List<(string Name, bool IsWifi, bool HasInternet)> AllConnections { get; private set; } = [];

    public NetworkMonitor() => _networkHandler = _ => RefreshState();

    public void Initialize()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;

        try
        {
            NetworkInformation.NetworkStatusChanged += _networkHandler;
            RefreshState();
        }
        catch (Exception ex)
        {
            try { NetworkInformation.NetworkStatusChanged -= _networkHandler; }
            catch (Exception unsubscribeException)
            {
                TADNLog.Log($"NetworkMonitor.Initialize unsubscribe: {unsubscribeException.Message}");
            }

            Interlocked.Exchange(ref _initialized, 0);
            TADNLog.Log($"NetworkMonitor.Initialize: {ex.Message}");
            throw;
        }
    }

    public void RefreshState()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        NetworkIconState previousState = CurrentState;

        try
        {
            NetworkSnapshot snapshot = BuildSnapshot();
            UpdateState(snapshot.State, snapshot.Bars, snapshot.Name, snapshot.Connections, previousState);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkMonitor.RefreshState: {ex.Message}");
            UpdateState(NetworkIconState.NoNetwork, 0, string.Empty, [], previousState);
        }
        finally
        {
            ReleaseWinRTProjectionReferencesAfterRefresh();
        }
    }

    private static NetworkSnapshot BuildSnapshot()
    {
        List<(string Name, bool IsWifi, bool HasInternet)> connections = [];
        ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile == null)
            return new NetworkSnapshot(NetworkIconState.NoNetwork, 0, string.Empty, connections);

        NetworkConnectivityLevel connectivity = profile.GetNetworkConnectivityLevel();
        string networkName = profile.ProfileName?.Trim() ?? string.Empty;

        // GetSignalBars returns 1-5 for Wi-Fi, null for non-Wi-Fi such as Ethernet
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
            int bars = Math.Clamp((int)signalBars.Value, 1, 4);
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
            return new NetworkSnapshot(state, bars, networkName, connections);
        }

        // No signal bars = Ethernet or other wired connection
        NetworkIconState ethernetState = connectivity switch
        {
            NetworkConnectivityLevel.InternetAccess => NetworkIconState.EthernetConnected,
            NetworkConnectivityLevel.LocalAccess
                or NetworkConnectivityLevel.ConstrainedInternetAccess => NetworkIconState.EthernetNoInternet,
            _ => NetworkIconState.EthernetDisconnected
        };
        return new NetworkSnapshot(ethernetState, 0, networkName, connections);
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
            _ => "No network connection"
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if (Interlocked.Exchange(ref _initialized, 0) != 0)
        {
            try { NetworkInformation.NetworkStatusChanged -= _networkHandler; }
            catch (Exception ex)
            {
                TADNLog.Log($"NetworkMonitor.Dispose unsubscribe: {ex.Message}");
            }
        }

        NetworkStateChanged = null;
    }

    private readonly record struct NetworkSnapshot(
        NetworkIconState State,
        int Bars,
        string Name,
        List<(string Name, bool IsWifi, bool HasInternet)> Connections);
}
