using Avalonia.Media;
using NetworkTrayAppDotNET.Models;

namespace NetworkTrayAppDotNET.Visuals;

/// <summary>Network's tray glyph: the connection state over a translucent full-signal backdrop.</summary>
internal sealed class NetworkTrayIcon(AppTheme? theme) : ITrayIconRenderer<TrayIconRenderInput>
{
    private const double BackdropOpacity = 0.55;

    private readonly AppTheme _theme = theme ?? AppTheme.Default;

    private readonly TrayIconRenderer _renderer = new(new TrayIconRenderOptions
    {
        IconFontFamilies = [GlyphCatalog.SEGOE_FLUENT_ICONS, GlyphCatalog.SEGOE_MDL2_ASSETS],
        Log = message => TADNLog.Log("NetworkTrayIcon." + message)
    });

    public bool IsLightTheme { get; set; }
    public TrayAppDotNETTrayIconStyle IconStyle { get; set; } = TrayAppDotNETTrayIconStyle.Dynamic;
    public NetworkIconState State { get; set; } = NetworkIconState.NoNetwork;
    public Color? CustomColor { get; set; }
    public Color? ConnectedColorOverride { get; set; }
    public Color? NoInternetColorOverride { get; set; }
    public Color? DisconnectedColorOverride { get; set; }

    public TrayIconRenderInput CreateRenderInput() =>
        new(ResolveGlyphs(State), ResolveColor(State), BackdropOpacity);

    public NativeIcon RenderIcon(TrayIconRenderInput input, int iconSize) => _renderer.Render(input, iconSize);

    private TrayIconGlyphLayer ResolveGlyphs(NetworkIconState state) =>
        state switch
        {
            NetworkIconState.Wifi0Bars or NetworkIconState.Wifi0BarsNoInternet => new TrayIconGlyphLayer(
                _theme.GlyphNetworkWifi4,
                _theme.GlyphNetworkWifi0),
            NetworkIconState.Wifi1Bar or NetworkIconState.Wifi1BarNoInternet => new TrayIconGlyphLayer(
                _theme.GlyphNetworkWifi4,
                _theme.GlyphNetworkWifi1),
            NetworkIconState.Wifi2Bars or NetworkIconState.Wifi2BarsNoInternet => new TrayIconGlyphLayer(
                _theme.GlyphNetworkWifi4,
                _theme.GlyphNetworkWifi2),
            NetworkIconState.Wifi3Bars or NetworkIconState.Wifi3BarsNoInternet => new TrayIconGlyphLayer(
                _theme.GlyphNetworkWifi4,
                _theme.GlyphNetworkWifi3),
            NetworkIconState.Wifi4Bars or NetworkIconState.Wifi4BarsNoInternet => new TrayIconGlyphLayer(
                BackdropGlyph: null,
                _theme.GlyphNetworkWifi4),
            NetworkIconState.WifiDisconnected => new TrayIconGlyphLayer(_theme.GlyphNetworkWifi4,
                _theme.GlyphNetworkWifi0),
            NetworkIconState.WifiConnecting => new TrayIconGlyphLayer(_theme.GlyphNetworkWifi4,
                _theme.GlyphNetworkWifi1),
            NetworkIconState.EthernetConnected or
                NetworkIconState.EthernetNoInternet or
                NetworkIconState.EthernetDisconnected => new TrayIconGlyphLayer(BackdropGlyph: null,
                    _theme.GlyphNetworkEthernet),
            _ => new TrayIconGlyphLayer(BackdropGlyph: null, _theme.GlyphNetworkNone)
        };

    private Color ResolveColor(NetworkIconState state)
    {
        if (IconStyle == TrayAppDotNETTrayIconStyle.Static)
            return CustomColor ?? _theme.Foreground.For(IsLightTheme);

        return state switch
        {
            NetworkIconState.NoNetwork => DisconnectedColor(),
            NetworkIconState.EthernetConnected => ConnectedColor(),
            NetworkIconState.EthernetNoInternet => NoInternetColor(),
            NetworkIconState.EthernetDisconnected => DisconnectedColor(),
            NetworkIconState.WifiDisconnected => DisconnectedColor(),
            NetworkIconState.WifiConnecting => NoInternetColor(),
            NetworkIconState.Wifi0Bars or NetworkIconState.Wifi1Bar or
                NetworkIconState.Wifi2Bars or NetworkIconState.Wifi3Bars or
                NetworkIconState.Wifi4Bars => ConnectedColor(),
            NetworkIconState.Wifi0BarsNoInternet or NetworkIconState.Wifi1BarNoInternet or
                NetworkIconState.Wifi2BarsNoInternet or NetworkIconState.Wifi3BarsNoInternet or
                NetworkIconState.Wifi4BarsNoInternet => NoInternetColor(),
            _ => ConnectedColor()
        };
    }

    private Color ConnectedColor() =>
        ConnectedColorOverride ?? _theme.NetworkConnectedTrayIconColor.For(IsLightTheme);

    private Color NoInternetColor() =>
        NoInternetColorOverride ?? _theme.NetworkNoInternetTrayIconColor.For(IsLightTheme);

    private Color DisconnectedColor() =>
        DisconnectedColorOverride ?? _theme.NetworkDisconnectedTrayIconColor.For(IsLightTheme);

    public void Dispose() => _renderer.Dispose();
}
