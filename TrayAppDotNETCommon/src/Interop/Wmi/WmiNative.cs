namespace TrayAppDotNETCommon.Interop.Wmi;

/// <summary>WMI constants used by <see cref="WmiConnection"/>; the COM natives live in <see cref="Ole32"/>.</summary>
public static class WmiNative
{
    public static readonly Guid ClsidWbemLocator = new("4590f811-1d3a-11d0-891f-00aa004b2e24");

    /// <summary>WBEM_S_TIMEDOUT: IEnumWbemClassObject.Next returned before an object arrived.</summary>
    public const int WbemSTimedOut = 0x40004;

    public const int WbemFlagReturnImmediately = 0x10;
    public const int WbemFlagForwardOnly = 0x20;
    public const int WbemQueryFlags = WbemFlagReturnImmediately | WbemFlagForwardOnly;

    /// <summary>WBEM_FLAG_CONNECT_USE_MAX_WAIT: ConnectServer gives up after two minutes instead of waiting forever.</summary>
    public const int WbemFlagConnectUseMaxWait = 0x80;

    public const uint RpcCAuthnWinNT = 10;
    public const uint RpcCAuthzNone = 0;
    public const uint RpcCAuthnLevelCall = 3;
    public const uint RpcCImpLevelImpersonate = 3;
    public const uint EoacNone = 0;

    /// <summary>Applies the call-level, impersonating blanket WMI needs to one proxy.</summary>
    public static int SetWmiProxyBlanket(IntPtr proxy) =>
        Ole32.CoSetProxyBlanket(
            proxy,
            RpcCAuthnWinNT,
            RpcCAuthzNone,
            IntPtr.Zero,
            RpcCAuthnLevelCall,
            RpcCImpLevelImpersonate,
            IntPtr.Zero,
            EoacNone);

    public static string FormatHResult(int hr) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"0x{unchecked((uint)hr):X8}");
}
