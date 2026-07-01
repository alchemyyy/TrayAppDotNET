using Avalonia.Media;
using NetworkTrayAppDotNET.Models;

namespace NetworkTrayAppDotNET.Visuals;

internal sealed class NetworkTrayIcon : IDisposable
{
    private const double BackdropOpacity = 0.55;

    private static readonly string[] IconFontFamilies =
    [
        GlyphCatalog.SEGOE_FLUENT_ICONS,
        GlyphCatalog.SEGOE_MDL2_ASSETS,
    ];

    private readonly AppTheme _theme;
    private readonly TrayIconRenderer _renderer;
    private bool _isDirty = true;
    private bool _isLightTheme;
    private TrayIconStyle _iconStyle = TrayIconStyle.Dynamic;
    private NetworkIconState _state = NetworkIconState.NoNetwork;
    private Color? _customColor;
    private Color? _connectedOverride;
    private Color? _noInternetOverride;
    private Color? _disconnectedOverride;

    public NetworkTrayIcon(AppTheme? theme)
    {
        _theme = theme ?? AppTheme.Default;
        _renderer = new TrayIconRenderer(new TrayIconRenderOptions
        {
            IconFontFamilies = IconFontFamilies,
            FallbackIcon = AppTheme.LoadAppNativeIcon,
            Log = message => TADNLog.Log("NetworkTrayIcon." + message),
        });
    }

    public bool IsLightTheme
    {
        get => _isLightTheme;
        set
        {
            if (_isLightTheme == value) return;
            _isLightTheme = value;
            _isDirty = true;
        }
    }

    public TrayIconStyle IconStyle
    {
        get => _iconStyle;
        set
        {
            if (_iconStyle == value) return;
            _iconStyle = value;
            _isDirty = true;
        }
    }

    public NetworkIconState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            _isDirty = true;
        }
    }

    public Color? CustomColor
    {
        get => _customColor;
        set
        {
            if (_customColor == value) return;
            _customColor = value;
            _isDirty = true;
        }
    }

    public Color? ConnectedColorOverride
    {
        get => _connectedOverride;
        set
        {
            if (_connectedOverride == value) return;
            _connectedOverride = value;
            _isDirty = true;
        }
    }

    public Color? NoInternetColorOverride
    {
        get => _noInternetOverride;
        set
        {
            if (_noInternetOverride == value) return;
            _noInternetOverride = value;
            _isDirty = true;
        }
    }

    public Color? DisconnectedColorOverride
    {
        get => _disconnectedOverride;
        set
        {
            if (_disconnectedOverride == value) return;
            _disconnectedOverride = value;
            _isDirty = true;
        }
    }

    public void InvalidateCache() => _isDirty = true;

    public NativeIcon? CreateIcon()
    {
        if (!TryCreateRenderInput(out TrayIconRenderInput? input) || input == null) return null;

        return _renderer.Render(input);
    }

    public bool TryCreateRenderInput(out TrayIconRenderInput? input)
    {
        input = null;
        if (!_isDirty) return false;

        _isDirty = false;
        input = new TrayIconRenderInput(ResolveGlyphs(_state), ResolveColor(_state), BackdropOpacity);
        return true;
    }

    public NativeIcon? RenderIcon(TrayIconRenderInput input) => _renderer.RenderOwned(input);

    private TrayIconGlyphLayer ResolveGlyphs(NetworkIconState state) =>
        state switch
        {
            NetworkIconState.Wifi0Bars or NetworkIconState.Wifi0BarsNoInternet => new TrayIconGlyphLayer(_theme.GlyphNetworkWifi4,
                _theme.GlyphNetworkWifi0),
            NetworkIconState.Wifi1Bar or NetworkIconState.Wifi1BarNoInternet => new TrayIconGlyphLayer(_theme.GlyphNetworkWifi4,
                _theme.GlyphNetworkWifi1),
            NetworkIconState.Wifi2Bars or NetworkIconState.Wifi2BarsNoInternet => new TrayIconGlyphLayer(_theme.GlyphNetworkWifi4,
                _theme.GlyphNetworkWifi2),
            NetworkIconState.Wifi3Bars or NetworkIconState.Wifi3BarsNoInternet => new TrayIconGlyphLayer(_theme.GlyphNetworkWifi4,
                _theme.GlyphNetworkWifi3),
            NetworkIconState.Wifi4Bars or NetworkIconState.Wifi4BarsNoInternet => new TrayIconGlyphLayer(null, _theme.GlyphNetworkWifi4),
            NetworkIconState.WifiDisconnected => new TrayIconGlyphLayer(_theme.GlyphNetworkWifi4, _theme.GlyphNetworkWifi0),
            NetworkIconState.WifiConnecting => new TrayIconGlyphLayer(_theme.GlyphNetworkWifi4, _theme.GlyphNetworkWifi1),
            NetworkIconState.EthernetConnected or
                NetworkIconState.EthernetNoInternet or
                NetworkIconState.EthernetDisconnected => new TrayIconGlyphLayer(null, _theme.GlyphNetworkEthernet),
            _ => new TrayIconGlyphLayer(null, _theme.GlyphNetworkNone),
        };

    private Color ResolveColor(NetworkIconState state)
    {
        if (_iconStyle == TrayIconStyle.Static)
            return _customColor ?? _theme.Foreground.For(_isLightTheme);

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
            _ => ConnectedColor(),
        };
    }

    private Color ConnectedColor() =>
        _connectedOverride ?? _theme.NetworkConnectedTrayIconColor.For(_isLightTheme);

    private Color NoInternetColor() =>
        _noInternetOverride ?? _theme.NetworkNoInternetTrayIconColor.For(_isLightTheme);

    private Color DisconnectedColor() =>
        _disconnectedOverride ?? _theme.NetworkDisconnectedTrayIconColor.For(_isLightTheme);

    public void Dispose() => _renderer.Dispose();
}
